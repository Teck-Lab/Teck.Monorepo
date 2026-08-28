using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.Auth;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Exercises the real Keycloak token, tenant registry, exchange and gateway edge path for each routed cluster.</summary>
[Collection("LocalIdentityKeycloak")]
public sealed class RoutedServiceAuthorizationTests(LocalIdentityKeycloakFixture fixture)
{
    private readonly LocalIdentityKeycloakFixture fixture = fixture;

    /// <summary>Ensures both routed clusters execute a real read endpoint for a signed-in reader.</summary>
    [Theory]
    [InlineData("/orders/00000000-0000-0000-0000-000000000001")]
    [InlineData("/price-lists")]
    public async Task GatewayRead_WhenTenantIsProvisioned_SucceedsForEachRoutedService(string path)
    {
        LocalIdentityTestInstance instance = await SelectedInstanceAsync().ConfigureAwait(false);
        string token = await LocalIdentityKeycloakFixture.GetReaderTokenAsync(instance).ConfigureAwait(false);
        string tenantId = TenantAwareSignInTests.ReadOrganizationIds(LocalIdentityKeycloakFixture.ReadToken(token)).Single();
        RoutedServiceFixture routedServices = await fixture.GetRoutedServiceFixtureAsync(instance).ConfigureAwait(false);
        Guid? seededOrderId = path.StartsWith("/orders", StringComparison.Ordinal)
            ? await routedServices.SeedOrderAsync(tenantId).ConfigureAwait(false)
            : null;
        using HttpClient client = routedServices.Gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string requestPath = path.StartsWith("/orders", StringComparison.Ordinal)
            ? $"/orders/{seededOrderId ?? throw new InvalidOperationException("The order fixture was not seeded.")}"
            : path;
        HttpResponseMessage response = await client.GetAsync(new Uri(requestPath, UriKind.Relative)).ConfigureAwait(false);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Gateway read returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        using JsonDocument responseBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(path.StartsWith("/orders", StringComparison.Ordinal) ? JsonValueKind.Object : JsonValueKind.Array, responseBody.RootElement.ValueKind);
    }

    /// <summary>Ensures each selected tenant writes and reads only its own pricing state through the real gateway.</summary>
    [Fact]
    public async Task GatewayRequest_WhenDeveloperSelectsEachProvisionedTenant_IsolatesPricingState()
    {
        LocalIdentityTestInstance instance = fixture.Provisioned;
        string token = await LocalIdentityKeycloakFixture.GetTokenAsync(instance, LocalIdentityKeycloakFixture.DeveloperUsername, LocalIdentityKeycloakFixture.DeveloperPassword).ConfigureAwait(false);
        string[] tenantIds = TenantAwareSignInTests.ReadOrganizationIds(LocalIdentityKeycloakFixture.ReadToken(token));
        Assert.Equal(2, tenantIds.Length);
        RoutedServiceFixture routedServices = await fixture.GetRoutedServiceFixtureAsync(instance).ConfigureAwait(false);
        using HttpClient client = routedServices.Gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string firstTenantPriceListName = $"tenant-one-price-list-{Guid.NewGuid():N}";
        string secondTenantPriceListName = $"tenant-two-price-list-{Guid.NewGuid():N}";
        await CreatePriceListAsync(client, tenantIds[0], firstTenantPriceListName).ConfigureAwait(false);
        await CreatePriceListAsync(client, tenantIds[1], secondTenantPriceListName).ConfigureAwait(false);
        IReadOnlyList<string> firstTenantPriceListNames = await GetPriceListNamesAsync(client, tenantIds[0]).ConfigureAwait(false);
        IReadOnlyList<string> secondTenantPriceListNames = await GetPriceListNamesAsync(client, tenantIds[1]).ConfigureAwait(false);
        Assert.Contains(firstTenantPriceListName, firstTenantPriceListNames);
        Assert.DoesNotContain(secondTenantPriceListName, firstTenantPriceListNames);
        Assert.Contains(secondTenantPriceListName, secondTenantPriceListNames);
        Assert.DoesNotContain(firstTenantPriceListName, secondTenantPriceListNames);
    }

    /// <summary>Ensures reader-only access is refused by each real management endpoint.</summary>
    [Theory]
    [InlineData("/orders/00000000-0000-0000-0000-000000000001/payment-retry", "{\"requestId\":\"retry-001\",\"paymentMethodToken\":\"pm-local\"}")]
    [InlineData("/price-lists", "{\"name\":\"Denied list\",\"currency\":\"USD\"}")]
    public async Task GatewayManagementRequest_WhenReaderLacksPermission_IsRefusedByRoutedService(string path, string body)
    {
        LocalIdentityTestInstance instance = fixture.Provisioned;
        string token = await LocalIdentityKeycloakFixture.GetReaderTokenAsync(instance).ConfigureAwait(false);
        RoutedServiceFixture routedServices = await fixture.GetRoutedServiceFixtureAsync(instance).ConfigureAwait(false);
        using HttpClient client = routedServices.Gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response = await client.PostAsync(new Uri(path, UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using JsonDocument responseBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal("Forbidden", responseBody.RootElement.GetProperty("title").GetString());
        Assert.Equal("Access denied due to insufficient permissions.", responseBody.RootElement.GetProperty("detail").GetString());
        Assert.Equal("authorization", responseBody.RootElement.GetProperty("errors")[0].GetProperty("name").GetString());
    }

    /// <summary>Ensures a reader cannot select the second tenant unless its Keycloak organization membership permits it.</summary>
    [Fact]
    public async Task GatewayRequest_WhenHeaderNamesNonMemberTenant_IsRefused()
    {
        string token = await LocalIdentityKeycloakFixture.GetReaderTokenAsync(fixture.Provisioned).ConfigureAwait(false);
        RoutedServiceFixture routedServices = await fixture.GetRoutedServiceFixtureAsync(fixture.Provisioned).ConfigureAwait(false);
        using HttpClient client = routedServices.Gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-TenantId", "teck-local-beta");
        HttpResponseMessage response = await client.GetAsync(new Uri("/orders/00000000-0000-0000-0000-000000000001", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("tenant.mismatch", await response.Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);
    }

    /// <summary>Confirms token exchange preserves signed tenant membership on every gateway-routed audience token.</summary>
    [Theory]
    [InlineData("order-api")]
    [InlineData("pricing-api")]
    public async Task TokenExchange_WhenRoutedAudienceIsRequested_ReturnsAudienceTokenWithTenantMembership(string audience)
    {
        LocalIdentityTestInstance instance = fixture.Provisioned;
        string token = await LocalIdentityKeycloakFixture.GetReaderTokenAsync(instance).ConfigureAwait(false);
        RoutedServiceFixture routedServices = await fixture.GetRoutedServiceFixtureAsync(instance).ConfigureAwait(false);
        IServiceTokenExchangeService service = routedServices.Gateway.Services.GetRequiredService<IServiceTokenExchangeService>();
        ServiceTokenResult exchanged = await service.ExchangeTokenAsync(token, audience, "fixture").ConfigureAwait(false);
        System.IdentityModel.Tokens.Jwt.JwtSecurityToken sourceToken = LocalIdentityKeycloakFixture.ReadToken(token);
        System.IdentityModel.Tokens.Jwt.JwtSecurityToken exchangedToken = LocalIdentityKeycloakFixture.ReadToken(exchanged.AccessToken);
        Assert.Contains(audience, exchangedToken.Audiences);
        Assert.Equal(TenantAwareSignInTests.ReadOrganizationIds(sourceToken), TenantAwareSignInTests.ReadOrganizationIds(exchangedToken));
    }

    private async Task<LocalIdentityTestInstance> SelectedInstanceAsync() =>
        string.Equals(Environment.GetEnvironmentVariable("TECK_LOCAL_IDENTITY_INSTANCE"), "unprovisioned", StringComparison.Ordinal)
            ? await fixture.GetUnprovisionedAsync().ConfigureAwait(false)
            : fixture.Provisioned;

    private static async Task CreatePriceListAsync(HttpClient client, string tenantId, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/price-lists", UriKind.Relative))
        {
            Content = new StringContent(JsonSerializer.Serialize(new { name, currency = "USD" }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-TenantId", tenantId);
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Tenant {tenantId} price-list creation returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
    }

    private static async Task<IReadOnlyList<string>> GetPriceListNamesAsync(HttpClient client, string tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/price-lists", UriKind.Relative));
        request.Headers.Add("X-TenantId", tenantId);
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Tenant {tenantId} price-list read returned {(int)response.StatusCode}: {responseBody}");
        using JsonDocument document = JsonDocument.Parse(responseBody);
        return document.RootElement.EnumerateArray().Select(priceList => priceList.GetProperty("name").GetString() ?? string.Empty).ToArray();
    }
}

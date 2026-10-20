using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Orders.Application.Database;
using Orders.Application.Orders.Responses;
using Orders.Domain.DomainEvents;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Orders.IntegrationTests;

/// <summary>Exercises direct Order.Host authorization and tenant persistence with real infrastructure.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakOrderAuthorizationTests(KeycloakTestRealmFixture keycloak, SharedTestcontainersFixture containers) : IAsyncLifetime
{
    private const string ClientId = "integration-test-client";
    private const string ClientSecret = "local-only-test-integration-token-client-secret-not-for-production";
    private const string OrderResourceClientId = "order-api";
    private const string OrderResourceClientSecret = "local-only-test-order-api-secret-not-for-production";
    private string? connectionString;
    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;

    public async ValueTask InitializeAsync()
    {
        connectionString = await containers.CreateSharedTestDatabaseAsync(typeof(OrderDbContext), "Order.Host").ConfigureAwait(false);
        factory = new OrderWebApplicationFactory(connectionString, containers.RabbitMqConnectionString, keycloak.Authority);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task GetOrder_WithInvalidToken_IsUnauthorized()
    {
        string readerToken = await keycloak.GetUserTokenAsync("integration-reader", "local-only-test-reader-password", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        using JsonDocument payload = ReadTokenPayload(readerToken);
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(keycloak.OrganizationId)
            .Options;
        Guid orderId;
        await using (var database = new OrderDbContext(options, null!))
        {
            Order order = CreateActionRequiredOrder(payload.RootElement.GetProperty("sub").GetString()!, keycloak.OrganizationId);
            database.Orders.Add(order);
            await database.SaveChangesAsync().ConfigureAwait(false);
            orderId = order.Id;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-signed-jwt");
        using HttpResponseMessage response = await client!.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RetryPayment_WithOtherRealKeycloakSubject_IsForbiddenWithoutPersistingRetry()
    {
        string ownerToken = await keycloak.GetUserTokenAsync("dev@teck.local", "local-only-dev-password-not-for-production", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        string managerToken = await keycloak.GetUserTokenAsync("integration-manager", "local-only-test-manager-password", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        using JsonDocument ownerPayload = ReadTokenPayload(ownerToken);
        using JsonDocument managerPayload = ReadTokenPayload(managerToken);
        string ownerSubject = ownerPayload.RootElement.GetProperty("sub").GetString()!;
        Assert.NotEqual(ownerSubject, managerPayload.RootElement.GetProperty("sub").GetString());
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(keycloak.OrganizationId)
            .Options;
        Guid orderId;
        await using (var database = new OrderDbContext(options, null!))
        {
            Order order = CreateActionRequiredOrder(ownerSubject, keycloak.OrganizationId);
            database.Orders.Add(order);
            await database.SaveChangesAsync().ConfigureAwait(false);
            orderId = order.Id;
        }

        string foreignToken = await keycloak.GetUserTokenAsync(
            "integration-foreign-member", "local-only-test-foreign-password", ClientId,
            clientSecret: ClientSecret).ConfigureAwait(false);
        using (JsonDocument foreignPayload = ReadTokenPayload(foreignToken))
        {
            Assert.Contains(keycloak.ForeignOrganizationId, foreignPayload.RootElement.GetProperty("organization")
                .EnumerateObject().Select(value => value.Value.GetProperty("id").GetString()));
        }

        client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignToken);
        using HttpResponseMessage foreignRead = await client.GetAsync($"/orders/{orderId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        using HttpResponseMessage sameRoleRead = await client.GetAsync($"/orders/{orderId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, sameRoleRead.StatusCode);

        string retryRequestId = $"other-subject-retry-{Guid.NewGuid():N}";
        using HttpResponseMessage deniedRetry = await client.PostAsJsonAsync($"/orders/{orderId}/payment-retry",
            new { requestId = retryRequestId, paymentMethodToken = "pm_test_replacement" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRetry.StatusCode);
        await using (var database = new OrderDbContext(options, null!))
        {
            Order persisted = await database.Orders.SingleAsync(order => order.Id == orderId).ConfigureAwait(false);
            Assert.Equal(PaymentState.ActionRequired, persisted.PaymentState);
            Assert.Null(persisted.RetryRequestId);
            Assert.False(persisted.HasRecordedRetryRequest(retryRequestId));
        }
    }

    [Fact]
    public async Task GetOrder_WithRealKeycloakMembership_EnforcesPermissionAndReturnsPersistedTenantOrder()
    {
        string readerToken = await keycloak.GetUserTokenAsync("integration-reader", "local-only-test-reader-password", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        string managerToken = await keycloak.GetUserTokenAsync("integration-manager", "local-only-test-manager-password", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        using JsonDocument payload = ReadTokenPayload(readerToken);
        JsonElement audience = payload.RootElement.GetProperty("aud");
        Assert.True(audience.ValueKind == JsonValueKind.String
            ? audience.GetString() == "order-api"
            : audience.EnumerateArray().Any(value => value.GetString() == "order-api"));
        JsonElement memberships = payload.RootElement.GetProperty("organization");
        string organizationId = memberships.EnumerateObject()
            .Select(value => value.Value.GetProperty("id").GetString())
            .Single(id => !string.IsNullOrWhiteSpace(id))!;
        Assert.Equal(keycloak.OrganizationId, organizationId);
        using JsonDocument managerPayload = ReadTokenPayload(managerToken);

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(organizationId)
            .Options;
        Guid readerOrderId;
        Guid managerOrderId;
        await using (var database = new OrderDbContext(options, null!))
        {
            Assert.True(payload.RootElement.TryGetProperty("sub", out JsonElement readerSubject));
            Assert.True(managerPayload.RootElement.TryGetProperty("sub", out JsonElement managerSubject));
            Order readerOrder = CreateActionRequiredOrder(readerSubject.GetString()!, organizationId);
            Order managerOrder = CreateActionRequiredOrder(managerSubject.GetString()!, organizationId);
            database.Orders.AddRange(readerOrder, managerOrder);
            await database.SaveChangesAsync().ConfigureAwait(false);
            readerOrderId = readerOrder.Id;
            managerOrderId = managerOrder.Id;
        }

        using HttpResponseMessage anonymous = await client!.GetAsync($"/orders/{readerOrderId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        string wrongAudienceToken = await keycloak.GetUserTokenAsync(
            "integration-reader", "local-only-test-reader-password", ClientId,
            clientSecret: ClientSecret, requestedScope: "pricing-api-audience").ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongAudienceToken);
        using HttpResponseMessage wrongAudienceRead = await client.GetAsync($"/orders/{readerOrderId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudienceRead.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        using HttpResponseMessage authorized = await client.GetAsync($"/orders/{readerOrderId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        OrderDto? retrieved = await authorized.Content.ReadFromJsonAsync<OrderDto>().ConfigureAwait(false);
        Assert.NotNull(retrieved);
        Assert.Equal(readerOrderId, retrieved.Id);
        Assert.Equal(10m, retrieved.Total);

        using HttpResponseMessage deniedRetry = await client.PostAsJsonAsync($"/orders/{readerOrderId}/payment-retry",
            new { requestId = $"reader-retry-{Guid.NewGuid():N}", paymentMethodToken = "pm_test_replacement" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRetry.StatusCode);
        await using (var database = new OrderDbContext(options, null!))
        {
            Order persisted = await database.Orders.SingleAsync(order => order.Id == readerOrderId).ConfigureAwait(false);
            Assert.Equal(organizationId, persisted.TenantId);
            Assert.Equal(retrieved.Total, persisted.Total);
            Assert.Equal(PaymentState.ActionRequired, persisted.PaymentState);
            Assert.Null(persisted.RetryRequestId);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        const string retryRequestId = "keycloak-manager-retry";
        using HttpResponseMessage acceptedRetry = await client.PostAsJsonAsync($"/orders/{managerOrderId}/payment-retry",
            new { requestId = retryRequestId, paymentMethodToken = "pm_test_replacement" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Accepted, acceptedRetry.StatusCode);
        await using (var database = new OrderDbContext(options, null!))
        {
            Order persisted = await database.Orders.SingleAsync(order => order.Id == managerOrderId).ConfigureAwait(false);
            Assert.Equal(organizationId, persisted.TenantId);
            Assert.Equal(PaymentState.Pending, persisted.PaymentState);
            Assert.Equal(retryRequestId, persisted.RetryRequestId);
            Assert.True(persisted.HasRecordedRetryRequest(retryRequestId));
        }
    }

    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync().ConfigureAwait(false);
        if (connectionString is not null) await containers.TruncateAllTablesAsync(connectionString).ConfigureAwait(false);
    }

    private static Order CreateActionRequiredOrder(string subject, string tenantId)
    {
        Order order = Order.Create(Guid.NewGuid(), subject, Guid.NewGuid(), tenantId,
            [new OrderLine(Guid.NewGuid(), "Keycloak integration order", 1, 10m)], 10m, "USD", $"keycloak-order-{Guid.NewGuid():N}");
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another payment method.", $"failure-{Guid.NewGuid():N}", order.CheckoutCorrelationId));
        return order;
    }

    private static JsonDocument ReadTokenPayload(string token)
    {
        string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=')));
    }

    private sealed class OrderWebApplicationFactory(string connectionString, string rabbitMqConnectionString, string authority) : WebApplicationFactory<Program>
    {
        static OrderWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:OrderWrite", connectionString);
            builder.UseSetting("ConnectionStrings:OrderRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitMqConnectionString);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.UseSetting("Keycloak:resource", OrderResourceClientId);
            builder.UseSetting("Keycloak:credentials:secret", OrderResourceClientSecret);
            builder.ConfigureTestServices(services => services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.MetadataAddress = authority + "/.well-known/openid-configuration";
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        options.MetadataAddress,
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever { RequireHttps = false });
                    options.MapInboundClaims = false;
                }));
        }
    }
}

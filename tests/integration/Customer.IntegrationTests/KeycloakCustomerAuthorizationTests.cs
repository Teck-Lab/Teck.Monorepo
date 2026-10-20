using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Customers.Application.Database;
using Customers.Application.Customers.Responses;
using Customers.Domain.Entities;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Exercises direct Customer.Host authorization against real Keycloak and PostgreSQL.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakCustomerAuthorizationTests : IAsyncLifetime
{
    private const string Reader = "integration-reader";
    private const string Manager = "integration-manager";
    private const string TokenClientId = "integration-test-client";
    private const string TokenClientSecret = "local-only-test-integration-token-client-secret-not-for-production";
    private const string ResourceClientId = "customer-api";
    private const string ResourceClientSecret = "local-only-test-customer-api-secret-not-for-production";
    private readonly KeycloakTestRealmFixture keycloak;
    private readonly SharedTestcontainersFixture containers;
    private string? connectionString;
    private CustomerWebApplicationFactory? factory;
    private HttpClient? client;

    /// <summary>Initializes a new instance of the <see cref="KeycloakCustomerAuthorizationTests"/> class.</summary>
    public KeycloakCustomerAuthorizationTests(KeycloakTestRealmFixture keycloak, SharedTestcontainersFixture containers)
    {
        this.keycloak = keycloak;
        this.containers = containers;
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        connectionString = await containers.CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host").ConfigureAwait(false);
        factory = new CustomerWebApplicationFactory(connectionString, keycloak.Authority);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task CustomerEndpoints_RejectAnonymousAndInvalidBearerTokens()
    {
        Guid protectedId = Guid.NewGuid();
        using HttpResponseMessage anonymousRead = await client!.GetAsync($"/customers/{protectedId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        using HttpResponseMessage anonymousProfile = await client.PutAsJsonAsync($"/customers/{protectedId}/profile", new { firstName = "No", lastName = "Token" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousProfile.StatusCode);
        using HttpResponseMessage anonymousAddress = await client.PostAsJsonAsync($"/customers/{protectedId}/addresses", AddressRequest()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAddress.StatusCode);
        using HttpResponseMessage invalidRead = await SendWithTokenAsync(HttpMethod.Get, $"/customers/{protectedId}", "not-a-signed-token").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidRead.StatusCode);
        using HttpResponseMessage invalidProfile = await SendWithTokenAsync(HttpMethod.Put, $"/customers/{protectedId}/profile", "not-a-signed-token", new { firstName = "No", lastName = "Token" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidProfile.StatusCode);
        using HttpResponseMessage invalidAddress = await SendWithTokenAsync(HttpMethod.Post, $"/customers/{protectedId}/addresses", "not-a-signed-token", AddressRequest()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidAddress.StatusCode);
    }

    [Fact]
    public async Task CustomerInOtherOrganization_IsNotFound()
    {
        string foreignToken = await keycloak.GetUserTokenAsync("integration-foreign-member", "local-only-test-foreign-password", TokenClientId, clientSecret: TokenClientSecret).ConfigureAwait(false);
        using JsonDocument foreignPayload = ReadTokenPayload(foreignToken);
        AssertAudience(foreignPayload.RootElement, ResourceClientId);
        string organizationId = ReadOrganizationId(foreignPayload.RootElement);
        Assert.NotEqual(keycloak.OrganizationId, organizationId);
        Guid customerId = await SeedCustomerAsync(keycloak.OrganizationId, "customer-owner").ConfigureAwait(false);

        client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignToken);
        using HttpResponseMessage readResponse = await client.GetAsync($"/customers/{customerId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Empty(await readResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        using HttpResponseMessage profileResponse = await client.PutAsJsonAsync($"/customers/{customerId}/profile", new { firstName = "Foreign", lastName = "Denied" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, profileResponse.StatusCode);
        using HttpResponseMessage addressResponse = await client.PostAsJsonAsync($"/customers/{customerId}/addresses", AddressRequest()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, addressResponse.StatusCode);
    }

    [Fact]
    public async Task CustomerEndpoints_EnforceRealKeycloakPermissionsAndPersistCustomerAddressByOrganization()
    {
        string readerToken = await keycloak.GetUserTokenAsync(Reader, "local-only-test-reader-password", TokenClientId, clientSecret: TokenClientSecret).ConfigureAwait(false);
        string managerToken = await keycloak.GetUserTokenAsync(Manager, "local-only-test-manager-password", TokenClientId, clientSecret: TokenClientSecret).ConfigureAwait(false);
        string foreignToken = await keycloak.GetUserTokenAsync("integration-foreign-member", "local-only-test-foreign-password", TokenClientId, clientSecret: TokenClientSecret).ConfigureAwait(false);
        using JsonDocument readerPayload = ReadTokenPayload(readerToken);
        using JsonDocument managerPayload = ReadTokenPayload(managerToken);
        using JsonDocument foreignPayload = ReadTokenPayload(foreignToken);
        AssertAudience(readerPayload.RootElement, ResourceClientId);
        AssertAudience(managerPayload.RootElement, ResourceClientId);
        AssertAudience(foreignPayload.RootElement, ResourceClientId);
        string organizationId = ReadOrganizationId(readerPayload.RootElement);
        Assert.Equal(keycloak.OrganizationId, organizationId);
        string managerOrganizationId = ReadOrganizationId(managerPayload.RootElement);
        Assert.Equal(organizationId, managerOrganizationId);
        string foreignOrganizationId = ReadOrganizationId(foreignPayload.RootElement);
        Assert.NotEqual(organizationId, foreignOrganizationId);

        Guid customerId = await SeedCustomerAsync(organizationId, readerPayload.RootElement.GetProperty("sub").GetString()!).ConfigureAwait(false);
        client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        using HttpResponseMessage allowedRead = await client.GetAsync($"/customers/{customerId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, allowedRead.StatusCode);
        using HttpResponseMessage readerManage = await client.PutAsJsonAsync($"/customers/{customerId}/profile", new { firstName = "Reader", lastName = "Denied" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, readerManage.StatusCode);
        using HttpResponseMessage readerAddress = await client.PostAsJsonAsync($"/customers/{customerId}/addresses", AddressRequest()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, readerAddress.StatusCode);


        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        using HttpResponseMessage managerRead = await client.GetAsync($"/customers/{customerId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, managerRead.StatusCode);
        using HttpResponseMessage profileResponse = await client.PutAsJsonAsync($"/customers/{customerId}/profile", new { firstName = "Updated", lastName = "Customer" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        CustomerDto? updated = await profileResponse.Content.ReadFromJsonAsync<CustomerDto>().ConfigureAwait(false);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.FirstName);
        Assert.Equal("Customer", updated.LastName);
        using HttpResponseMessage addressResponse = await client.PostAsJsonAsync($"/customers/{customerId}/addresses", AddressRequest()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, addressResponse.StatusCode);
        AddressDto? address = await addressResponse.Content.ReadFromJsonAsync<AddressDto>().ConfigureAwait(false);
        Assert.NotNull(address);
        Assert.Equal("10 Downing Street", address!.Line1);

        var options = new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(connectionString).UseTeckCloudTenant(organizationId).Options;
        await using var database = new CustomerDbContext(options, null!);
        Customer persisted = await database.Customers.Include(customer => customer.Addresses).SingleAsync(customer => customer.Id == customerId).ConfigureAwait(false);
        Assert.Equal(organizationId, persisted.TenantId);
        Assert.Equal("Updated", persisted.FirstName);
        Assert.Equal("Customer", persisted.LastName);
        Assert.Single(persisted.Addresses);
        Assert.Equal(address!.Id, persisted.Addresses.Single().Id);
        Assert.Equal("10 Downing Street", persisted.Addresses.Single().Line1);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync().ConfigureAwait(false);
        }

        if (connectionString is not null)
        {
            await containers.TruncateAllTablesAsync(connectionString).ConfigureAwait(false);
        }
    }

    private async Task<Guid> SeedCustomerAsync(string tenantId, string subjectId)
    {
        var options = new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(connectionString).UseTeckCloudTenant(tenantId).Options;
        await using var database = new CustomerDbContext(options, null!);
        var customer = Customer.Create(tenantId, subjectId, $"{Guid.NewGuid():N}@example.com", "Initial", "Customer");
        database.Customers.Add(customer);
        await database.SaveChangesAsync().ConfigureAwait(false);
        return customer.Id;
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client!.SendAsync(request).ConfigureAwait(false);
    }

    private static object AddressRequest() => new { line1 = "10 Downing Street", line2 = (string?)null, city = "London", postalCode = "SW1A 2AA", country = "GB" };

    private static string ReadOrganizationId(JsonElement payload)
    {
        JsonElement memberships = payload.GetProperty("organization");
        return memberships.EnumerateObject()
            .Select(value => value.Value.GetProperty("id").GetString())
            .Single(id => !string.IsNullOrWhiteSpace(id))!;
    }

    private static void AssertAudience(JsonElement payload, string expected)
    {
        JsonElement audience = payload.GetProperty("aud");
        Assert.True(audience.ValueKind == JsonValueKind.String
            ? audience.GetString() == expected
            : audience.EnumerateArray().Any(value => value.GetString() == expected));
    }

    private static JsonDocument ReadTokenPayload(string token)
    {
        string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=')));
    }

    private sealed class CustomerWebApplicationFactory(string connectionString, string authority) : WebApplicationFactory<Program>
    {
        static CustomerWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CustomerWrite", connectionString);
            builder.UseSetting("ConnectionStrings:CustomerRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.UseSetting("Keycloak:resource", ResourceClientId);
            builder.UseSetting("Keycloak:credentials:secret", ResourceClientSecret);
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
                }));
        }
    }
}

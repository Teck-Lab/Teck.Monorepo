using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Customers.Application.Database;
using Customers.Application.EventHandlers.IntegrationEvents;
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
using Customers.Host.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Exercises direct Customer.Host authorization against real Keycloak and PostgreSQL.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakCustomerAuthorizationTests : IAsyncLifetime
{
    private static SubscriptionPaymentConfirmedHandler CreatePaymentHandler(CustomerDbContext context,
        IOrganizationProvisioner organizations, SubscriptionPaymentVerificationKeys billingKeys) =>
        new(new CustomerWriteRepository<Tenant, Guid>(context, new Microsoft.AspNetCore.Http.HttpContextAccessor()),
            new UnitOfWork<CustomerDbContext>(context), organizations, billingKeys);
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
    public async Task PaidOrganization_WithRealKeycloakAdmin_LinksOnlyVerifiedReservedOwner()
    {
        string buyerToken = await keycloak.GetUserTokenAsync("integration-no-access", "local-only-test-no-access-password",
            TokenClientId, clientSecret: TokenClientSecret, requestedScope: "customer-api-audience").ConfigureAwait(false);
        using var buyer = ReadTokenPayload(buyerToken);
        string ownerSubject = buyer.RootElement.GetProperty("sub").GetString()!;
        var tenantId = Guid.NewGuid();
        var identifier = $"paid-business-{tenantId:N}";
        using var http = new HttpClient();
        var provisioner = new Customers.Host.Infrastructure.KeycloakOrganizationProvisioner(http,
            Microsoft.Extensions.Options.Options.Create(new Customers.Host.Infrastructure.KeycloakOrganizationOptions
            {
                ServerUrl = keycloak.Authority[..keycloak.Authority.LastIndexOf("/realms/", StringComparison.Ordinal)],
                Realm = KeycloakTestRealmFixture.Realm,
                AdminClientId = "customer-org-provisioner",
                AdminClientSecret = "local-only-test-customer-org-provisioner-secret-not-for-production",
            }));

        var organizationId = await provisioner.EnsureOwnerOrganizationAsync(tenantId, identifier, ownerSubject,
            CancellationToken.None).ConfigureAwait(false);
        Assert.NotEqual(Guid.Empty, organizationId);
        Assert.Equal(organizationId, await provisioner.EnsureOwnerOrganizationAsync(tenantId, identifier,
            ownerSubject, CancellationToken.None).ConfigureAwait(false));

        string updatedToken = await keycloak.GetUserTokenAsync("integration-no-access", "local-only-test-no-access-password",
            TokenClientId, clientSecret: TokenClientSecret, requestedScope: "customer-api-audience organization:*").ConfigureAwait(false);
        using var parsed = ReadTokenPayload(updatedToken);
        Assert.Contains(parsed.RootElement.GetProperty("organization").EnumerateObject(),
            membership => membership.Value.GetProperty("id").GetString() == organizationId.ToString("D"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.EnsureOwnerOrganizationAsync(Guid.NewGuid(),
            identifier, ownerSubject, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task SignedPaidConfirmation_PersistsRealOwnerOrganizationButStillDeniesUnreadyTenant()
    {
        string buyerToken = await keycloak.GetUserTokenAsync("integration-no-access", "local-only-test-no-access-password",
            TokenClientId, clientSecret: TokenClientSecret, requestedScope: "customer-api-audience").ConfigureAwait(false);
        using var buyer = ReadTokenPayload(buyerToken);
        string ownerSubject = buyer.RootElement.GetProperty("sub").GetString()!;
        var tenantId = Guid.NewGuid();
        var reservation = Tenant.CreatePendingReservation(tenantId, $"paid-owner-{tenantId:N}", ownerSubject);
        await using (var scope = factory!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            database.Tenants.Add(reservation);
            await database.SaveChangesAsync().ConfigureAwait(false);
        }

        var confirmedAt = DateTimeOffset.UtcNow;
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            TenantId = tenantId, TenantIdentifier = reservation.Identifier, SourceOrderId = Guid.NewGuid(),
            SubscriptionId = Guid.NewGuid(), PlanId = "Business", PlanVersion = "2026-10",
            PaymentMethod = "captured", AmountMinor = 10000, Currency = "USD", DatabaseStrategy = "dedicated",
            DatabaseProvider = "postgres", MaxLocations = 2, ConfirmedAtUtc = confirmedAt,
            ValidFromUtc = confirmedAt, ValidUntilUtc = confirmedAt.AddYears(1),
        };
        using var privateKey = RSA.Create(2048);
        using var trustedKeys = new SubscriptionPaymentVerificationKeys([privateKey.ExportSubjectPublicKeyInfoPem()]);
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        using var http = new HttpClient();
        var provisioner = new Customers.Host.Infrastructure.KeycloakOrganizationProvisioner(http,
            Microsoft.Extensions.Options.Options.Create(new Customers.Host.Infrastructure.KeycloakOrganizationOptions
            {
                ServerUrl = keycloak.Authority[..keycloak.Authority.LastIndexOf("/realms/", StringComparison.Ordinal)],
                Realm = KeycloakTestRealmFixture.Realm, AdminClientId = "customer-org-provisioner",
                AdminClientSecret = "local-only-test-customer-org-provisioner-secret-not-for-production",
            }));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = CreatePaymentHandler(scope.ServiceProvider.GetRequiredService<CustomerDbContext>(), provisioner, trustedKeys);
            await handler.Handle(paid, CancellationToken.None).ConfigureAwait(false);
            await handler.Handle(paid, CancellationToken.None).ConfigureAwait(false);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var persisted = await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
                .AsNoTracking().SingleAsync(item => item.Id == tenantId).ConfigureAwait(false);
            Assert.Equal("paid_pending_readiness", persisted.Status);
            Assert.Equal(paid.SourceOrderId, persisted.PurchaseOrderId);
            Assert.Equal(paid.SubscriptionId, persisted.SubscriptionId);
            Assert.NotNull(persisted.KeycloakOrganizationId);
            Assert.False(persisted.DatabaseReady);
            Assert.False(persisted.LicenseReady);

            string refreshed = await keycloak.GetUserTokenAsync("integration-no-access", "local-only-test-no-access-password",
                TokenClientId, clientSecret: TokenClientSecret,
                requestedScope: "customer-api-audience organization:*").ConfigureAwait(false);
            using var membership = ReadTokenPayload(refreshed);
            Assert.Contains(membership.RootElement.GetProperty("organization").EnumerateObject(),
                organization => organization.Value.GetProperty("id").GetString() == persisted.KeycloakOrganizationId.Value.ToString("D"));
        }
    }

    [Fact]
    public async Task PurchaseReservation_WithRealServiceToken_AllowsBillingOnlyAndRejectsUserToken()
    {
        var reservation = Tenant.CreatePendingReservation(Guid.NewGuid(), $"new-tenant-{Guid.NewGuid():N}", "buyer-subject");
        await using (var scope = factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            db.Tenants.Add(reservation);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        using var tokenClient = new HttpClient();
        using var tokenResponse = await tokenClient.PostAsync($"{keycloak.Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "billing-api",
                ["client_secret"] = "local-only-test-billing-api-secret-not-for-production",
                ["scope"] = "tenant-purchase customer-api-audience",
            })).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        using var payload = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        var serviceToken = payload.RootElement.GetProperty("access_token").GetString()!;
        using var parsed = ReadTokenPayload(serviceToken);
        Assert.Equal("billing-api", parsed.RootElement.GetProperty("azp").GetString());
        Assert.Equal("billing-api", parsed.RootElement.GetProperty("billing_service_identity").GetString());
        Assert.Contains("tenant-purchase", parsed.RootElement.GetProperty("scope").GetString()!);
        AssertAudience(parsed.RootElement, "customer-api");

        var url = $"/internal/purchase-reservations/{reservation.Id:D}";
        using var serviceResponse = await SendWithTokenAsync(HttpMethod.Get, url, serviceToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, serviceResponse.StatusCode);
        string userToken = await keycloak.GetUserTokenAsync("integration-reader", "local-only-test-reader-password",
            TokenClientId, clientSecret: TokenClientSecret, requestedScope: "customer-api-audience").ConfigureAwait(false);
        using var userResponse = await SendWithTokenAsync(HttpMethod.Get, url, userToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, userResponse.StatusCode);
    }

    [Fact]
    public async Task Onboarding_WithSignedBuyerWithoutOrganization_ReservesPendingTenant()
    {
        string token = await keycloak.GetUserTokenAsync("integration-pending-buyer", "local-only-test-pending-buyer-password",
            TokenClientId, clientSecret: TokenClientSecret, requestedScope: "customer-api-audience").ConfigureAwait(false);
        using var parsed = ReadTokenPayload(token);
        Assert.False(parsed.RootElement.TryGetProperty("tenant_id", out _));
        if (parsed.RootElement.TryGetProperty("organization", out var organizations))
        {
            Assert.Equal(0, organizations.EnumerateObject().Count());
        }

        using var response = await SendWithTokenAsync(HttpMethod.Post, "/tenants/onboarding", token,
            new { DisplayName = $"New Buyer {Guid.NewGuid():N}" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        Assert.Equal("pending", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal(parsed.RootElement.GetProperty("sub").GetString(), payload.RootElement.GetProperty("ownerSubjectId").GetString());
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

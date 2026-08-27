extern alias GatewayHost;
extern alias OrderHost;
extern alias PricingHost;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Customers.Application.Database;
using Customers.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Orders.Application.Database;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Database.EFCore;
using Yarp.ReverseProxy.Forwarder;
using Xunit;
using ITenantDatabaseStrategyResolver = GatewayHost::Gateway.Public.Edge.ITenantDatabaseStrategyResolver;
using TenantDbStrategyResult = GatewayHost::Gateway.Public.Edge.TenantDbStrategyResult;

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
        LocalIdentityTestInstance instance = SelectedInstance();
        string token = await LocalIdentityKeycloakFixture.GetReaderTokenAsync(instance).ConfigureAwait(false);
        string issuer = LocalIdentityKeycloakFixture.ReadToken(token).Issuer;
        IReadOnlyList<SecurityKey> signingKeys = await GetPublishedSigningKeysAsync(issuer).ConfigureAwait(false);
        string tenantId = TenantAwareSignInTests.ReadOrganizationIds(LocalIdentityKeycloakFixture.ReadToken(token)).Single();
        using var routedServices = await RoutedServiceHosts.CreateAsync(
            instance,
            issuer,
            signingKeys,
            path.StartsWith("/orders", StringComparison.Ordinal) ? tenantId : null).ConfigureAwait(false);
        using var gateway = new RealIdentityGatewayFactory(instance, routedServices, issuer, signingKeys);
        using HttpClient client = gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        string requestPath = path.StartsWith("/orders", StringComparison.Ordinal)
            ? $"/orders/{routedServices.SeededOrderId ?? throw new InvalidOperationException("The order fixture was not seeded.")}"
            : path;
        HttpResponseMessage response = await client.GetAsync(new Uri(requestPath, UriKind.Relative)).ConfigureAwait(false);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Gateway read returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");

        using JsonDocument responseBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal(path.StartsWith("/orders", StringComparison.Ordinal) ? JsonValueKind.Object : JsonValueKind.Array, responseBody.RootElement.ValueKind);
    }

    /// <summary>Ensures each selected tenant writes and reads only its own pricing state through the real gateway.</summary>
    [Fact]
    public async Task GatewayRequest_WhenDeveloperSelectsEachProvisionedTenant_IsolatesPricingState()
    {
        LocalIdentityTestInstance instance = fixture.Provisioned;
        string token = await LocalIdentityKeycloakFixture.GetTokenAsync(
            instance,
            LocalIdentityKeycloakFixture.DeveloperUsername,
            LocalIdentityKeycloakFixture.DeveloperPassword).ConfigureAwait(false);
        string issuer = LocalIdentityKeycloakFixture.ReadToken(token).Issuer;
        IReadOnlyList<SecurityKey> signingKeys = await GetPublishedSigningKeysAsync(issuer).ConfigureAwait(false);
        string[] tenantIds = TenantAwareSignInTests.ReadOrganizationIds(LocalIdentityKeycloakFixture.ReadToken(token));
        Assert.Equal(2, tenantIds.Length);
        using var routedServices = await RoutedServiceHosts.CreateAsync(instance, issuer, signingKeys).ConfigureAwait(false);
        using var gateway = new RealIdentityGatewayFactory(instance, routedServices, issuer, signingKeys);
        using HttpClient client = gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
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
        string issuer = LocalIdentityKeycloakFixture.ReadToken(token).Issuer;
        IReadOnlyList<SecurityKey> signingKeys = await GetPublishedSigningKeysAsync(issuer).ConfigureAwait(false);
        using var routedServices = await RoutedServiceHosts.CreateAsync(instance, issuer, signingKeys).ConfigureAwait(false);
        using var gateway = new RealIdentityGatewayFactory(instance, routedServices, issuer, signingKeys);
        using HttpClient client = gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.PostAsync(
            new Uri(path, UriKind.Relative),
            new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);

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
        string issuer = LocalIdentityKeycloakFixture.ReadToken(token).Issuer;
        IReadOnlyList<SecurityKey> signingKeys = await GetPublishedSigningKeysAsync(issuer).ConfigureAwait(false);
        using var routedServices = await RoutedServiceHosts.CreateAsync(fixture.Provisioned, issuer, signingKeys).ConfigureAwait(false);
        using var gateway = new RealIdentityGatewayFactory(fixture.Provisioned, routedServices, issuer, signingKeys);
        using HttpClient client = gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
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
        using var gateway = new RealIdentityGatewayFactory(instance);
        IServiceTokenExchangeService service = gateway.Services.GetRequiredService<IServiceTokenExchangeService>();

        ServiceTokenResult exchanged = await service.ExchangeTokenAsync(token, audience, "fixture").ConfigureAwait(false);

        System.IdentityModel.Tokens.Jwt.JwtSecurityToken sourceToken = LocalIdentityKeycloakFixture.ReadToken(token);
        System.IdentityModel.Tokens.Jwt.JwtSecurityToken exchangedToken = LocalIdentityKeycloakFixture.ReadToken(exchanged.AccessToken);

        Assert.Contains(audience, exchangedToken.Audiences);
        Assert.Equal(
            TenantAwareSignInTests.ReadOrganizationIds(sourceToken),
            TenantAwareSignInTests.ReadOrganizationIds(exchangedToken));
    }

    private LocalIdentityTestInstance SelectedInstance() =>
        string.Equals(Environment.GetEnvironmentVariable("TECK_LOCAL_IDENTITY_INSTANCE"), "unprovisioned", StringComparison.Ordinal)
            ? fixture.Unprovisioned
            : fixture.Provisioned;

    private static async Task<IReadOnlyList<SecurityKey>> GetPublishedSigningKeysAsync(string issuer)
    {
        using var client = new HttpClient();
        string jwks = await client.GetStringAsync($"{issuer}/protocol/openid-connect/certs").ConfigureAwait(false);
        return new JsonWebKeySet(jwks).GetSigningKeys().ToArray();
    }

    private static async Task CreatePriceListAsync(HttpClient client, string tenantId, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/price-lists", UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { name, currency = "USD" }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("X-TenantId", tenantId);

        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Tenant {tenantId} price-list creation returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
    }

    private static async Task<IReadOnlyList<string>> GetPriceListNamesAsync(HttpClient client, string tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/price-lists", UriKind.Relative));
        request.Headers.Add("X-TenantId", tenantId);

        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Tenant {tenantId} price-list read returned {(int)response.StatusCode}: {responseBody}");
        using JsonDocument document = JsonDocument.Parse(responseBody);
        return document.RootElement.EnumerateArray()
            .Select(priceList => priceList.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
    }

    private sealed class RealIdentityGatewayFactory(
        LocalIdentityTestInstance instance,
        RoutedServiceHosts? routedServices = null,
        string? tokenIssuer = null,
        IReadOnlyList<SecurityKey>? signingKeys = null) : WebApplicationFactory<GatewayHost::Program>
    {
        static RealIdentityGatewayFactory() => JasperFx.CommandLine.JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            string baseAddress = string.IsNullOrWhiteSpace(tokenIssuer)
                ? instance.Keycloak.GetBaseAddress().TrimEnd('/')
                : tokenIssuer[..tokenIssuer.IndexOf("/realms/", StringComparison.OrdinalIgnoreCase)];
            builder.UseEnvironment("Development");
            builder.UseSetting("Keycloak:realm", LocalIdentityKeycloakFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", baseAddress);
            builder.UseSetting("Keycloak:resource", LocalIdentityKeycloakFixture.GatewayClientId);
            builder.UseSetting("Keycloak:credentials:secret", LocalIdentityKeycloakFixture.GatewayClientSecret);
            builder.UseSetting("Keycloak:TokenEndpoint", $"{baseAddress}/realms/{LocalIdentityKeycloakFixture.Realm}/protocol/openid-connect/token");
            builder.UseSetting("Services:CustomerApi:Url", "http://customer-test.invalid");
            builder.ConfigureTestServices(services =>
            {
                if (!string.IsNullOrWhiteSpace(tokenIssuer) && signingKeys is not null)
                {
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        // Testcontainers advertises a bridge-network issuer that differs from the host address
                        // used to reach the container. Validate the real token strictly using that issuer's JWKS.
                        options.RequireHttpsMetadata = false;
                        options.MetadataAddress = null;
                        options.ConfigurationManager = null!;
                        options.MapInboundClaims = false;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKeys = signingKeys,
                            ValidateIssuer = true,
                            ValidIssuer = tokenIssuer,
                            ValidateAudience = true,
                            ValidAudience = LocalIdentityKeycloakFixture.GatewayClientId,
                            RequireAudience = true,
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.Zero,
                        };
                    });
                }

                services.Replace(ServiceDescriptor.Singleton<ITenantDatabaseStrategyResolver>(new TenantRegistryResolver(instance.CustomerConnectionString)));
                if (routedServices is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(routedServices.CreateForwarderFactory()));
                }
            });
        }
    }

    private sealed class TenantRegistryResolver(string connectionString) : ITenantDatabaseStrategyResolver
    {
        public async Task<TenantDbStrategyResult> ResolveAsync(string tenantId, string? serviceName, CancellationToken ct)
        {
            if (!Guid.TryParse(tenantId, out Guid parsedTenantId))
            {
                return TenantDbStrategyResult.Fail(400, "tenant.not_found", "tenant_id must be a valid GUID.");
            }

            var options = new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(connectionString).Options;
            await using var database = new CustomerDbContext(options, null!);
            Tenant? tenant = await database.Tenants.SingleOrDefaultAsync(item => item.Id == parsedTenantId, ct).ConfigureAwait(false);
            return tenant is null
                ? TenantDbStrategyResult.Fail(404, "tenant.not_found", "Tenant not found.")
                : TenantDbStrategyResult.Ok(tenant.DatabaseStrategy);
        }
    }

    private sealed class RoutedServiceHosts : IDisposable
    {
        private readonly OrderServiceFactory order;
        private readonly PricingServiceFactory pricing;

        private RoutedServiceHosts(
            LocalIdentityTestInstance instance,
            string issuer,
            IReadOnlyList<SecurityKey> signingKeys)
        {
            order = new OrderServiceFactory(instance, issuer, signingKeys);
            pricing = new PricingServiceFactory(instance, issuer, signingKeys);
        }

        internal static async Task<RoutedServiceHosts> CreateAsync(
            LocalIdentityTestInstance instance,
            string issuer,
            IReadOnlyList<SecurityKey> signingKeys,
            string? orderTenantId = null)
        {
            var services = new RoutedServiceHosts(instance, issuer, signingKeys);
            if (!string.IsNullOrWhiteSpace(orderTenantId))
            {
                services.SeededOrderId = await SeedOrderAsync(instance.OrderConnectionString, orderTenantId).ConfigureAwait(false);
            }

            return services;
        }

        internal IForwarderHttpClientFactory CreateForwarderFactory() => new RoutedServiceForwarderHttpClientFactory(
            order.Server.CreateHandler(),
            pricing.Server.CreateHandler());

        internal Guid? SeededOrderId { get; private set; }

        public void Dispose()
        {
            pricing.Dispose();
            order.Dispose();
        }

        private static async Task<Guid> SeedOrderAsync(string connectionString, string tenantId)
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseNpgsql(connectionString)
                .UseTeckCloudTenant(tenantId)
                .Options;
            await using var database = new OrderDbContext(options, null!);
            Order seededOrder = Order.Create(
                Guid.NewGuid(),
                "reader-subject",
                Guid.NewGuid(),
                tenantId,
                [new OrderLine(Guid.NewGuid(), "Routed service order", 1, 10m)],
                10m,
                "USD",
                $"routed-service-order-{Guid.NewGuid():N}");
            database.Orders.Add(seededOrder);
            await database.SaveChangesAsync().ConfigureAwait(false);
            return seededOrder.Id;
        }
    }

    private sealed class RoutedServiceForwarderHttpClientFactory(
        HttpMessageHandler orderHandler,
        HttpMessageHandler pricingHandler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(
            string.Equals(context.ClusterId, "order", StringComparison.OrdinalIgnoreCase)
                ? orderHandler
                : string.Equals(context.ClusterId, "pricing", StringComparison.OrdinalIgnoreCase)
                    ? pricingHandler
                    : throw new InvalidOperationException($"Unexpected routed cluster '{context.ClusterId}'."),
            disposeHandler: false);
    }

    private sealed class OrderServiceFactory(
        LocalIdentityTestInstance instance,
        string issuer,
        IReadOnlyList<SecurityKey> signingKeys) : WebApplicationFactory<OrderHost::Program>
    {
        static OrderServiceFactory() => JasperFx.CommandLine.JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureRoutedServiceHost(
            builder,
            instance,
            issuer,
            signingKeys,
            "Order",
            instance.OrderConnectionString,
            "order-api",
            "local-only-order-api-secret-not-for-production");
    }

    private sealed class PricingServiceFactory(
        LocalIdentityTestInstance instance,
        string issuer,
        IReadOnlyList<SecurityKey> signingKeys) : WebApplicationFactory<PricingHost::Program>
    {
        static PricingServiceFactory() => JasperFx.CommandLine.JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureRoutedServiceHost(
            builder,
            instance,
            issuer,
            signingKeys,
            "Pricing",
            instance.PricingConnectionString,
            "pricing-api",
            "local-only-pricing-api-secret-not-for-production");
    }

    private static void ConfigureRoutedServiceHost(
        IWebHostBuilder builder,
        LocalIdentityTestInstance instance,
        string issuer,
        IReadOnlyList<SecurityKey> signingKeys,
        string connectionName,
        string connectionString,
        string resource,
        string clientSecret)
    {
        string baseAddress = issuer[..issuer.IndexOf("/realms/", StringComparison.OrdinalIgnoreCase)];
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{connectionName}Write", connectionString);
        builder.UseSetting($"ConnectionStrings:{connectionName}Read", connectionString);
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("ConnectionStrings:rabbitmq", instance.RabbitMqConnectionString);
        builder.UseSetting("Keycloak:realm", LocalIdentityKeycloakFixture.Realm);
        builder.UseSetting("Keycloak:auth-server-url", baseAddress);
        builder.UseSetting("Keycloak:resource", resource);
        builder.UseSetting("Keycloak:credentials:secret", clientSecret);
        builder.ConfigureTestServices(services => ConfigureStrictJwt(services, issuer, signingKeys, resource));
    }

    private static void ConfigureStrictJwt(
        IServiceCollection services,
        string issuer,
        IReadOnlyList<SecurityKey> signingKeys,
        string audience) => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.RequireHttpsMetadata = false;
            options.MetadataAddress = null;
            options.ConfigurationManager = null!;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                RequireAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        });

    private static readonly Guid ReadOrderId = Guid.Parse("00000000-0000-0000-0000-000000000001");
}

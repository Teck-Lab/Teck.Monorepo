extern alias GatewayHost;
extern alias OrderHost;
extern alias PricingHost;

using System.Net.Http.Headers;
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
using SharedKernel.Infrastructure.Database.EFCore;
using Yarp.ReverseProxy.Forwarder;
using ITenantDatabaseStrategyResolver = GatewayHost::Gateway.Public.Edge.ITenantDatabaseStrategyResolver;
using TenantDbStrategyResult = GatewayHost::Gateway.Public.Edge.TenantDbStrategyResult;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Owns the in-process routed-service hosts and gateway for one immutable identity-fixture state.</summary>
internal sealed class RoutedServiceFixture : IDisposable
{
    private readonly RoutedServiceHosts routedServices;

    private RoutedServiceFixture(LocalIdentityTestInstance instance, string issuer, IReadOnlyList<SecurityKey> signingKeys)
    {
        routedServices = new RoutedServiceHosts(instance, issuer, signingKeys);
        Gateway = new RealIdentityGatewayFactory(instance, routedServices, issuer, signingKeys);
    }

    /// <summary>Gets the gateway configured with the real routed-service forwarders.</summary>
    internal RealIdentityGatewayFactory Gateway { get; }

    /// <summary>Creates one order row for the calling test without rebuilding the shared hosts.</summary>
    internal Task<Guid> SeedOrderAsync(string tenantId) => routedServices.SeedOrderAsync(tenantId);

    /// <summary>Creates the immutable shared hosts after retrieving the selected Keycloak instance's signing keys.</summary>
    internal static async Task<RoutedServiceFixture> CreateAsync(LocalIdentityTestInstance instance)
    {
        string token = await LocalIdentityKeycloakFixture.GetReaderTokenAsync(instance).ConfigureAwait(false);
        string issuer = LocalIdentityKeycloakFixture.ReadToken(token).Issuer;
        IReadOnlyList<SecurityKey> signingKeys = await GetPublishedSigningKeysAsync(issuer).ConfigureAwait(false);
        return new RoutedServiceFixture(instance, issuer, signingKeys);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Gateway.Dispose();
        routedServices.Dispose();
    }

    private static async Task<IReadOnlyList<SecurityKey>> GetPublishedSigningKeysAsync(string issuer)
    {
        using var client = new HttpClient();
        string jwks = await client.GetStringAsync($"{issuer}/protocol/openid-connect/certs").ConfigureAwait(false);
        return new JsonWebKeySet(jwks).GetSigningKeys().ToArray();
    }
}

/// <summary>Hosts the real gateway with strict token validation and test-only routed-service forwarders.</summary>
internal sealed class RealIdentityGatewayFactory(
    LocalIdentityTestInstance instance,
    RoutedServiceHosts? routedServices = null,
    string? tokenIssuer = null,
    IReadOnlyList<SecurityKey>? signingKeys = null) : WebApplicationFactory<GatewayHost::Program>
{
    static RealIdentityGatewayFactory() => JasperFx.CommandLine.JasperFxEnvironment.AutoStartHost = true;

    /// <inheritdoc />
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

/// <summary>Resolves the real tenant registry against the fixture's customer database.</summary>
internal sealed class TenantRegistryResolver(string connectionString) : ITenantDatabaseStrategyResolver
{
    /// <inheritdoc />
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

/// <summary>Owns one Order host and one Pricing host backed by the fixture's migrated databases.</summary>
internal sealed class RoutedServiceHosts : IDisposable
{
    private readonly LocalIdentityTestInstance instance;
    private readonly OrderServiceFactory order;
    private readonly PricingServiceFactory pricing;

    internal RoutedServiceHosts(LocalIdentityTestInstance instance, string issuer, IReadOnlyList<SecurityKey> signingKeys)
    {
        this.instance = instance;
        order = new OrderServiceFactory(instance, issuer, signingKeys);
        pricing = new PricingServiceFactory(instance, issuer, signingKeys);
    }

    internal IForwarderHttpClientFactory CreateForwarderFactory() => new RoutedServiceForwarderHttpClientFactory(
        order.Server.CreateHandler(),
        pricing.Server.CreateHandler());

    internal async Task<Guid> SeedOrderAsync(string tenantId)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(instance.OrderConnectionString)
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

    /// <inheritdoc />
    public void Dispose()
    {
        pricing.Dispose();
        order.Dispose();
    }
}

/// <summary>Routes gateway forwarder clients only to the two real in-process service handlers.</summary>
internal sealed class RoutedServiceForwarderHttpClientFactory(
    HttpMessageHandler orderHandler,
    HttpMessageHandler pricingHandler) : IForwarderHttpClientFactory
{
    /// <inheritdoc />
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(
        string.Equals(context.ClusterId, "order", StringComparison.OrdinalIgnoreCase)
            ? orderHandler
            : string.Equals(context.ClusterId, "pricing", StringComparison.OrdinalIgnoreCase)
                ? pricingHandler
                : throw new InvalidOperationException($"Unexpected routed cluster '{context.ClusterId}'."),
        disposeHandler: false);
}

/// <summary>Builds the shared Order host with strict real-Keycloak authentication.</summary>
internal sealed class OrderServiceFactory(
    LocalIdentityTestInstance instance,
    string issuer,
    IReadOnlyList<SecurityKey> signingKeys) : WebApplicationFactory<OrderHost::Program>
{
    static OrderServiceFactory() => JasperFx.CommandLine.JasperFxEnvironment.AutoStartHost = true;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder) => RoutedServiceHostConfiguration.Configure(
        builder,
        instance,
        issuer,
        signingKeys,
        "Order",
        instance.OrderConnectionString,
        "order-api",
        "local-only-order-api-secret-not-for-production");
}

/// <summary>Builds the shared Pricing host with strict real-Keycloak authentication.</summary>
internal sealed class PricingServiceFactory(
    LocalIdentityTestInstance instance,
    string issuer,
    IReadOnlyList<SecurityKey> signingKeys) : WebApplicationFactory<PricingHost::Program>
{
    static PricingServiceFactory() => JasperFx.CommandLine.JasperFxEnvironment.AutoStartHost = true;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder) => RoutedServiceHostConfiguration.Configure(
        builder,
        instance,
        issuer,
        signingKeys,
        "Pricing",
        instance.PricingConnectionString,
        "pricing-api",
        "local-only-pricing-api-secret-not-for-production");
}

/// <summary>Applies the common strict-authentication configuration to a routed service host.</summary>
internal static class RoutedServiceHostConfiguration
{
    internal static void Configure(
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
}

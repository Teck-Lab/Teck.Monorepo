extern alias GatewayHost;
extern alias OrderHost;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using Orders.Application.Database;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Events;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Wolverine.Tracking;
using Xunit;
using Yarp.ReverseProxy.Forwarder;
using ZiggyCreatures.Caching.Fusion;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Proves the Keycloak realm contract across the real gateway and order-host request path.</summary>
[Collection("SharedTestcontainers")]
public sealed class KeycloakSubjectOwnershipTests : IDisposable
{
    private const string GatewayClientId = "public-gateway";
    private const string GatewayClientSecret = "dev-secret-change-me";
    private const string OrderAudience = "order-api";
    private static readonly string[] ExchangeFormFields = ["audience", "grant_type", "scope", "subject_token", "subject_token_type"];
    private readonly SharedTestcontainersFixture fixture;
    private readonly string orderConnectionString;
    private readonly TokenEndpointStub tokenEndpoint = new();
    private readonly OrderWebApplicationFactory orderFactory;
    private readonly WebApplicationFactory<GatewayHost::Program> gatewayFactory;

    /// <summary>Builds the real Order and Gateway hosts against deterministic in-process identity seams.</summary>
    public KeycloakSubjectOwnershipTests(SharedTestcontainersFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
        orderConnectionString = fixture
            .CreateSharedTestDatabaseAsync(typeof(OrderDbContext), "Order.Host")
            .GetAwaiter()
            .GetResult();

        orderFactory = new OrderWebApplicationFactory(orderConnectionString, fixture.RabbitMqConnectionString, tokenEndpoint);
        gatewayFactory = new GatewayWebApplicationFactory(orderFactory.Server.CreateHandler(), tokenEndpoint);
    }

    /// <summary>
    /// Mocks only Keycloak's issuer/token endpoint while exercising Gateway.Public,
    /// ServiceTokenExchangeService, YARP, Order.Host JWT validation, and the retry endpoint.
    /// </summary>
    [Fact]
    public async Task ImportedRealm_MockedExchangeAndGatewayRetry_PreserveTenantBoundOwnership()
    {
        JsonElement gatewayRealm = ReadPublicGatewayClient();
        AssertAudienceMapper(gatewayRealm, GatewayClientId);
        AssertAudienceMapper(gatewayRealm, OrderAudience);
        AssertTenantMapper(gatewayRealm);

        string tenantId = Guid.NewGuid().ToString();
        string otherTenantId = Guid.NewGuid().ToString();
        string ownerSubject = "owner-" + Guid.NewGuid().ToString("N");
        string secondSubject = "other-" + Guid.NewGuid().ToString("N");
        Guid orderId = await SeedActionRequiredOrderAsync(ownerSubject, tenantId).ConfigureAwait(false);
        await SeedGatewayTenantCacheAsync(tenantId).ConfigureAwait(false);
        await SeedGatewayTenantCacheAsync(otherTenantId).ConfigureAwait(false);

        string ownerInbound = DeterministicKeycloak.IssueInbound(GatewayClientId, ownerSubject, tenantId);
        AssertClaims(ownerInbound, GatewayClientId, ownerSubject, tenantId);

        using HttpClient gateway = gatewayFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRetryAsync(gateway, orderId, "missing-bearer").ConfigureAwait(false)).StatusCode);
        var (ownerResponse, ownerTracking) = await SendRetryWithOutboxTrackingAsync(gateway, orderId, "owner-retry", ownerInbound).ConfigureAwait(false);
        Assert.True(
            ownerResponse.StatusCode == HttpStatusCode.Accepted,
            $"Expected 202 but received {(int)ownerResponse.StatusCode}: {await ownerResponse.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        Assert.Single(ownerTracking.Sent.MessagesOf<PaymentRetryRequestedIntegrationEvent>());

        HttpResponseMessage duplicateResponse = await SendRetryAsync(gateway, orderId, "owner-retry", ownerInbound).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Accepted, duplicateResponse.StatusCode);

        string secondInbound = DeterministicKeycloak.IssueInbound(GatewayClientId, secondSubject, tenantId);
        HttpResponseMessage secondSubjectResponse = await SendRetryAsync(gateway, orderId, "second-subject", secondInbound).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, secondSubjectResponse.StatusCode);

        string changedTenantInbound = DeterministicKeycloak.IssueInbound(GatewayClientId, ownerSubject, otherTenantId);
        HttpResponseMessage changedTenantResponse = await SendRetryAsync(gateway, orderId, "changed-tenant", changedTenantInbound).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, changedTenantResponse.StatusCode);

        Order persisted = await ReadOrderAsync(orderId, tenantId).ConfigureAwait(false);
        Assert.Equal(PaymentState.Pending, persisted.PaymentState);
        Assert.True(persisted.HasRecordedRetryRequest("owner-retry"));
        Assert.Equal(1, RetryCount(persisted));
        Assert.False(persisted.HasRecordedRetryRequest("second-subject"));
        Assert.False(persisted.HasRecordedRetryRequest("changed-tenant"));

        Assert.Equal(4, tokenEndpoint.Exchanges.Count);
        Assert.All(tokenEndpoint.Exchanges, exchange =>
        {
            AssertExactExchangeForm(exchange);
            AssertClaims(exchange.ExchangedToken, OrderAudience, exchange.Subject, exchange.TenantId);
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        gatewayFactory.Dispose();
        orderFactory.Dispose();
        fixture.TruncateAllTablesAsync(orderConnectionString).GetAwaiter().GetResult();
    }

    private async Task<Guid> SeedActionRequiredOrderAsync(string subject, string tenantId)
    {
        var order = Order.Create(
            Guid.NewGuid(),
            subject,
            Guid.NewGuid(),
            tenantId,
            [new OrderLine(Guid.NewGuid(), "Gateway-owned order", 1, 10m)],
            10m,
            "USD",
            "gateway-checkout");
        Assert.NotNull(order.ApplyPaymentFailure("generic-decline", "Use another payment method.", "gateway-failed", "gateway-checkout"));

        using AsyncServiceScope scope = orderFactory.Services.CreateAsyncScope();
        SetTenant(scope.ServiceProvider, tenantId);
        OrderDbContext db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private async Task<Order> ReadOrderAsync(Guid orderId, string tenantId)
    {
        using AsyncServiceScope scope = orderFactory.Services.CreateAsyncScope();
        SetTenant(scope.ServiceProvider, tenantId);
        OrderDbContext db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        return await db.Orders.SingleAsync(order => order.Id == orderId);
    }

    private static void SetTenant(IServiceProvider services, string tenantId)
    {
        var tenantSetter = services.GetRequiredService<IMultiTenantContextSetter>();
        tenantSetter.MultiTenantContext = new MultiTenantContext<TenantDetails>(new TenantDetails
        {
            Id = tenantId,
            Identifier = tenantId,
            Name = tenantId,
            IsActive = true,
        });
    }

    private async Task SeedGatewayTenantCacheAsync(string tenantId)
    {
        IFusionCache cache = gatewayFactory.Services.GetRequiredService<IFusionCache>();
        await cache.SetAsync($"tenant-db-strategy:{tenantId}:order", new TenantDatabaseInfoRpcResult
        {
            Found = true,
            TenantId = tenantId,
            Identifier = tenantId,
            DatabaseStrategy = "shared",
            DatabaseProvider = "postgres",
        });
    }

    private static async Task<HttpResponseMessage> SendRetryAsync(HttpClient client, Guid orderId, string requestId, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/orders/{orderId}/payment-retry")
        {
            Content = JsonContent.Create(new { requestId, paymentMethodToken = "token-replacement" }),
        };
        if (bearer is not null)
        {
            request.Headers.Authorization = new("Bearer", bearer);
        }

        return await client.SendAsync(request);
    }

    private async Task<(HttpResponseMessage Response, ITrackedSession Tracking)> SendRetryWithOutboxTrackingAsync(
        HttpClient client,
        Guid orderId,
        string requestId,
        string bearer)
    {
        HttpResponseMessage? response = null;
        TrackedSessionConfiguration trackingConfiguration = orderFactory.Services.TrackActivity()
            .IncludeExternalTransports()
            .Timeout(TimeSpan.FromSeconds(10))
            .WaitForCondition(new PaymentRetryPublicationCondition());

        Func<IMessageContext, Task> execute = async _ =>
        {
            response = await SendRetryAsync(client, orderId, requestId, bearer).ConfigureAwait(false);
        };
        ITrackedSession tracking = await trackingConfiguration.ExecuteAndWaitAsync(execute).ConfigureAwait(false);
        return (response!, tracking);
    }

    private static int RetryCount(Order order) => order.ProcessedTransitionKeys
        .Split('|', StringSplitOptions.RemoveEmptyEntries)
        .Count(key => key.StartsWith("payment-retry:", StringComparison.Ordinal));

    private static JsonElement ReadPublicGatewayClient()
    {
        string root = FindRepositoryRoot();
        using JsonDocument realm = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "aspire", "Teck.AppHost", "realms", "teck-realm.json")));
        return realm.RootElement.GetProperty("clients").EnumerateArray()
            .Single(client => client.GetProperty("clientId").GetString() == GatewayClientId)
            .Clone();
    }

    private static void AssertAudienceMapper(JsonElement gateway, string audience)
    {
        JsonElement mapper = gateway.GetProperty("protocolMappers").EnumerateArray()
            .Single(item => item.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper" &&
                item.GetProperty("config").GetProperty("included.client.audience").GetString() == audience);
        Assert.Equal("true", mapper.GetProperty("config").GetProperty("access.token.claim").GetString());
    }

    private static void AssertTenantMapper(JsonElement gateway)
    {
        JsonElement mapper = gateway.GetProperty("protocolMappers").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "tenant_id");
        JsonElement config = mapper.GetProperty("config");
        Assert.Equal("oidc-usermodel-attribute-mapper", mapper.GetProperty("protocolMapper").GetString());
        Assert.Equal("tenant_id", config.GetProperty("user.attribute").GetString());
        Assert.Equal("tenant_id", config.GetProperty("claim.name").GetString());
        Assert.Equal("true", config.GetProperty("access.token.claim").GetString());
    }

    private static void AssertClaims(string token, string audience, string subject, string tenantId)
    {
        JwtSecurityToken parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(audience, parsed.Audiences);
        Assert.Equal(subject, parsed.Subject);
        Assert.Equal(tenantId, parsed.Claims.Single(claim => claim.Type == "tenant_id").Value);
        Assert.DoesNotContain(parsed.Claims, claim => claim.Type == "customer_id");
    }

    private static void AssertExactExchangeForm(TokenExchange exchange)
    {
        Assert.Equal("POST", exchange.Method);
        Assert.Equal("/realms/teck/protocol/openid-connect/token", exchange.Path);
        Assert.Equal(
            ExchangeFormFields.Order(StringComparer.Ordinal).ToArray(),
            exchange.Form.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", exchange.Form["grant_type"]);
        Assert.Equal(
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(GatewayClientId + ":" + GatewayClientSecret)),
            exchange.Authorization);
        Assert.Equal(exchange.InboundToken, exchange.Form["subject_token"]);
        Assert.Equal("urn:ietf:params:oauth:token-type:access_token", exchange.Form["subject_token_type"]);
        Assert.Equal(OrderAudience, exchange.Form["audience"]);
        Assert.Equal("organization:*", exchange.Form["scope"]);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "aspire", "Teck.AppHost", "realms", "teck-realm.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the committed Teck realm JSON.");
    }

    private sealed class GatewayWebApplicationFactory(HttpMessageHandler orderHandler, TokenEndpointStub tokenEndpoint)
        : WebApplicationFactory<GatewayHost::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Keycloak:realm", "teck");
            builder.UseSetting("Keycloak:auth-server-url", DeterministicKeycloak.IssuerBaseUrl);
            builder.UseSetting("Keycloak:resource", GatewayClientId);
            builder.UseSetting("Keycloak:credentials:secret", GatewayClientSecret);
            builder.UseSetting("Keycloak:TokenEndpoint", tokenEndpoint.Endpoint.ToString());
            builder.UseSetting("Services:CustomerApi:Url", "http://customer.unused.test");
            builder.ConfigureTestServices(services =>
            {
                ConfigureStrictJwt(services, GatewayClientId);
                services.AddHttpClient(string.Empty)
                    .ConfigurePrimaryHttpMessageHandler(() => tokenEndpoint);
                services.AddHttpClient("KeycloakTokenClient")
                    .ConfigurePrimaryHttpMessageHandler(() => tokenEndpoint);
                services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(
                    new TestForwarderHttpClientFactory(orderHandler)));
            });
        }
    }

    private sealed class OrderWebApplicationFactory(
        string connectionString,
        string rabbitConnectionString,
        TokenEndpointStub tokenEndpoint) : WebApplicationFactory<OrderHost::Program>
    {
        static OrderWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:OrderWrite", connectionString);
            builder.UseSetting("ConnectionStrings:OrderRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);
            builder.UseSetting("Keycloak:realm", "teck");
            builder.UseSetting("Keycloak:auth-server-url", DeterministicKeycloak.IssuerBaseUrl);
            builder.UseSetting("Keycloak:resource", OrderAudience);
            builder.UseSetting("Keycloak:credentials:secret", GatewayClientSecret);
            builder.ConfigureTestServices(services =>
            {
                ConfigureHeaderTenantResolution(services);
                ConfigureStrictJwt(services, OrderAudience);
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new KeycloakEndpointHandlerFilter(tokenEndpoint));
            });
        }
    }

    private static void ConfigureStrictJwt(IServiceCollection services, string audience)
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
                IssuerSigningKey = DeterministicKeycloak.SigningKey,
                ValidateIssuer = true,
                ValidIssuer = DeterministicKeycloak.Issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                RequireAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        });
    }

    private static void ConfigureHeaderTenantResolution(IServiceCollection services)
    {
        var tenants = services.AddMultiTenant<TenantDetails>()
            .WithDelegateStrategy(context => Task.FromResult<string?>(
                (context as HttpContext)?.Request.Headers["X-TenantId"].ToString()));
        services.AddHttpContextAccessor();
        services.AddScoped<IMultiTenantStore<TenantDetails>, HeaderTenantStore>();
        tenants.WithStore<HeaderTenantStore>(ServiceLifetime.Scoped);
        services.AddSingleton<IStartupFilter, TestTenantResolutionStartupFilter>();
    }

    private sealed class TestTenantResolutionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseMultiTenant();
            next(app);
        };
    }

    private sealed class TestForwarderHttpClientFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
    }

    private sealed class PaymentRetryPublicationCondition : ITrackedCondition
    {
        private bool observed;

        public void Record(EnvelopeRecord record)
        {
            observed |= record.MessageEventType == MessageEventType.Sent &&
                record.Envelope.Message is PaymentRetryRequestedIntegrationEvent;
        }

        public bool IsCompleted() => observed;
    }

    private sealed class KeycloakEndpointHandlerFilter(TokenEndpointStub tokenEndpoint) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.AdditionalHandlers.Insert(0, new KeycloakEndpointRoutingHandler(tokenEndpoint));
        };
    }

    private sealed class KeycloakEndpointRoutingHandler(TokenEndpointStub tokenEndpoint) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            string.Equals(request.RequestUri?.Host, "keycloak.test", StringComparison.OrdinalIgnoreCase)
                ? tokenEndpoint.HandleAsync(request, cancellationToken)
                : base.SendAsync(request, cancellationToken);
    }

    private sealed class TokenEndpointStub : HttpMessageHandler
    {
        internal Uri Endpoint { get; } = new("http://keycloak.test/realms/teck/protocol/openid-connect/token");

        internal List<TokenExchange> Exchanges { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            HandleAsync(request, cancellationToken);

        internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(
                    pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                    pair => Uri.UnescapeDataString((pair.Length == 2 ? pair[1] : string.Empty).Replace('+', ' ')),
                    StringComparer.Ordinal);
            if (string.Equals(form["grant_type"], "urn:ietf:params:oauth:grant-type:uma-ticket", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { result = true }),
                };
            }

            string inbound = form["subject_token"];
            JwtSecurityToken parsed = new JwtSecurityTokenHandler().ReadJwtToken(inbound);
            string subject = parsed.Subject!;
            string tenantId = parsed.Claims.Single(claim => claim.Type == "tenant_id").Value;
            string exchanged = DeterministicKeycloak.IssueInbound(OrderAudience, subject, tenantId);
            Exchanges.Add(new TokenExchange(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                form,
                request.Headers.Authorization?.ToString(),
                inbound,
                exchanged,
                subject,
                tenantId));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { access_token = exchanged, expires_in = 300, token_type = "Bearer" }),
            };
        }
    }

    private sealed record TokenExchange(
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Form,
        string? Authorization,
        string InboundToken,
        string ExchangedToken,
        string Subject,
        string TenantId);

    private static class DeterministicKeycloak
    {
        internal const string IssuerBaseUrl = "http://keycloak.test";
        internal const string Issuer = IssuerBaseUrl + "/realms/teck";
        internal static SymmetricSecurityKey SigningKey { get; } = new(Encoding.UTF8.GetBytes("teck-integration-test-signing-key-32b"));

        internal static string IssueInbound(string audience, string subject, string tenantId)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim("tenant_id", tenantId),
            };
            var token = new JwtSecurityToken(
                Issuer,
                audience,
                claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Gateway.Edge;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Infrastructure.Auth;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Gateway.WebCustomer.IntegrationTests;

/// <summary>Exercises the customer gateway's public contract and strict YARP route allowlist.</summary>
public sealed class CustomerGatewayTests
{
    /// <summary>Aspire can probe the customer gateway independently of upstream services.</summary>
    [Fact]
    public async Task CustomerGateway_LivenessIsPublic()
    {
        using var factory = new CustomerFactory(new HttpClientHandler());
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
    }

    /// <summary>Product reads exchange the user token; operator/pricing routes cannot enter the customer gateway.</summary>
    [Fact]
    public async Task CustomerGateway_ExposesProductReadsOnly()
    {
        using var upstream = new TestServer(new WebHostBuilder().Configure(app =>
            app.Run(context => context.Response.WriteAsJsonAsync(new
            {
                Authorization = context.Request.Headers.Authorization.ToString(),
                Tenant = context.Request.Headers["X-TenantId"].ToString(),
                Strategy = context.Request.Headers["X-Tenant-DbStrategy"].ToString(),
            }))));
        using var factory = new CustomerFactory(upstream.CreateHandler());
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mock-Tenant", "tenant-a");
        client.DefaultRequestHeaders.Authorization = new("Bearer", "raw-user-bearer");
        client.DefaultRequestHeaders.Add("X-Tenant-DbStrategy", "dedicated:attacker");

        foreach (string path in new[] { "/api/v1/catalog/products", "/api/v1/catalog/products/abc" })
        {
            HttpResponseMessage response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument echo = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Bearer exchanged-catalog-bearer", echo.RootElement.GetProperty("authorization").GetString());
            Assert.Equal("tenant-a", echo.RootElement.GetProperty("tenant").GetString());
            Assert.Equal("shared", echo.RootElement.GetProperty("strategy").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/admin/catalog/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync("/api/v1/admin/pricing/price-lists/x/prices/y", null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/api/v1/catalog/products", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/products")).StatusCode);
    }

    /// <summary>Customer documentation is public while authenticated operations challenge anonymous requests.</summary>
    [Fact]
    public async Task CustomerGateway_PublicDocsContainOnlyReachableProductReads()
    {
        using var factory = new CustomerFactory(new HttpClientHandler());
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/catalog/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/scalar/v1/")).StatusCode);
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");
        Assert.Equal(2, paths.EnumerateObject().Count());
        Assert.True(paths.GetProperty("/api/v1/catalog/products").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/catalog/products/{productId}").TryGetProperty("get", out _));
        Assert.False(paths.GetProperty("/api/v1/catalog/products").TryGetProperty("post", out _));
    }

    private sealed class CustomerFactory(HttpMessageHandler upstream) : WebApplicationFactory<Program>
    {
        static CustomerFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:CustomerApi:Url"] = "http://customer-test.invalid",
                ["Testing:UseMockAuthentication"] = "true",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                });
                services.Replace(ServiceDescriptor.Singleton<IServiceTokenExchangeService>(new TestExchange()));
                services.Replace(ServiceDescriptor.Singleton<ITenantDatabaseStrategyResolver>(new TestStrategy()));
                services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(new TestForwarder(upstream)));
            });
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string tenant = Request.Headers["X-Mock-Tenant"].ToString();
            if (tenant.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim("tenant_id", tenant) }, "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class TestExchange : IServiceTokenExchangeService
    {
        public Task<ServiceTokenResult> ExchangeTokenAsync(string subjectToken, string audience, string tenantId, CancellationToken cancellationToken = default)
        {
            Assert.Equal("raw-user-bearer", subjectToken);
            Assert.Equal("catalog-api", audience);
            Assert.Equal("tenant-a", tenantId);
            return Task.FromResult(new ServiceTokenResult("exchanged-catalog-bearer", DateTime.UtcNow.AddMinutes(5)));
        }
    }

    private sealed class TestStrategy : ITenantDatabaseStrategyResolver
    {
        public Task<TenantDbStrategyResult> ResolveAsync(string tenantId, string? serviceName, CancellationToken ct) =>
            Task.FromResult(TenantDbStrategyResult.Ok("shared"));
    }

    private sealed class TestForwarder(HttpMessageHandler handler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
    }
}

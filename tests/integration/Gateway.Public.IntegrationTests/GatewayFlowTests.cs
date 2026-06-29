// <copyright file="GatewayFlowTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharedKernel.Infrastructure.Auth;
using Xunit;
using Yarp.ReverseProxy.Forwarder;
using Gateway.Public.Edge;

namespace Gateway.Public.IntegrationTests;

/// <summary>
/// End-to-end integration tests for the public gateway edge pipeline.
/// Boots <see cref="Program"/> via <see cref="WebApplicationFactory{TEntryPoint}"/> and
/// replaces the real upstream (order cluster), authentication (Keycloak JWT), token exchange
/// (Keycloak) and tenant-strategy resolver (customer gRPC) with in-process test doubles so
/// that the full edge pipeline (HeaderFirewall → ResolveTenant → ResolveDbStrategy →
/// ExchangeToken → YARP transform) can be exercised without external infrastructure.
///
/// YARP upstream stub approach: An in-memory <see cref="TestServer"/> built from a minimal
/// ASP.NET Core app echoes the received <c>X-TenantId</c>, <c>X-Tenant-DbStrategy</c> and
/// <c>Authorization</c> headers back as JSON. YARP is directed to use this echo server via
/// a custom <see cref="IForwarderHttpClientFactory"/> singleton that returns the echo server's
/// <see cref="TestServer.CreateHandler"/> message handler for every forwarding request.
/// This avoids any real network connection while still exercising the full YARP transform
/// pipeline.
/// </summary>
public sealed class GatewayFlowTests : IClassFixture<GatewayFlowTests.GatewayFixture>
{
    private readonly GatewayFixture fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayFlowTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared gateway test fixture.</param>
    public GatewayFlowTests(GatewayFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>
    /// An authenticated request for tenant-a must be forwarded with the resolved
    /// <c>X-TenantId</c>, a non-empty <c>X-Tenant-DbStrategy</c>, and an exchanged
    /// <c>Authorization: Bearer ...</c> header.
    /// </summary>
    [Fact]
    public async Task AuthenticatedRequest_ForwardsTenantAndDbStrategyAndExchangedBearer()
    {
        using HttpClient client = fixture.CreateMockUserClient("tenant-a");

        HttpResponseMessage response = await client.GetAsync("/orders/123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EchoedHeaders? echoed = await response.Content.ReadFromJsonAsync<EchoedHeaders>();
        Assert.NotNull(echoed);
        Assert.Equal("tenant-a", echoed!.TenantId);
        Assert.False(string.IsNullOrEmpty(echoed.TenantDbStrategy));
        Assert.StartsWith("Bearer ", echoed.Authorization, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An unauthenticated request must be rejected with 401 by the authorization middleware
    /// before any tenant-resolution or token-exchange logic runs.
    /// </summary>
    [Fact]
    public async Task Unauthenticated_Returns401_BeforeTenantLogic()
    {
        using HttpClient client = fixture.Factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.GetAsync("/orders/123");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// An authenticated request that claims tenant-a in the token but presents
    /// <c>X-TenantId: tenant-b</c> in the request header must be rejected with 403
    /// (tenant.mismatch) by the ResolveTenant edge step.
    /// </summary>
    [Fact]
    public async Task TokenTenantMismatch_Returns403()
    {
        using HttpClient client = fixture.CreateMockUserClient("tenant-a");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/123");
        request.Headers.Add("X-TenantId", "tenant-b");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// DTO that the in-process echo upstream serialises as JSON so tests can assert
    /// exactly which headers the gateway forwarded.
    /// </summary>
    internal sealed record EchoedHeaders(string TenantId, string TenantDbStrategy, string Authorization);

    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shared fixture that owns the echo <see cref="TestServer"/> and the gateway
    /// <see cref="WebApplicationFactory{TEntryPoint}"/>. Created once per test class.
    /// </summary>
    public sealed class GatewayFixture : IAsyncLifetime
    {
        private IHost? echoHost;
        private GatewayWebApplicationFactory? webFactory;

        /// <summary>Gets the configured gateway <see cref="WebApplicationFactory{TEntryPoint}"/>.</summary>
        internal WebApplicationFactory<Program> Factory =>
            webFactory ?? throw new InvalidOperationException("GatewayFixture not initialized.");

        /// <summary>Creates an authenticated HTTP client whose requests claim the given tenant.</summary>
        /// <param name="tenantId">The tenant id to impersonate.</param>
        /// <returns>A disposable <see cref="HttpClient"/>.</returns>
        internal HttpClient CreateMockUserClient(string tenantId)
        {
            HttpClient client = Factory.CreateClient(
                new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add(
                MockBearerAuthenticationHandler.MockTenantIdHeader, tenantId);
            return client;
        }

        /// <inheritdoc/>
        public async ValueTask InitializeAsync()
        {
            // Build the in-process echo server (replaces the real order cluster).
            echoHost = await new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.Configure(app =>
                    {
                        app.Run(async ctx =>
                        {
                            var echoed = new EchoedHeaders(
                                TenantId: ctx.Request.Headers["X-TenantId"].ToString(),
                                TenantDbStrategy: ctx.Request.Headers["X-Tenant-DbStrategy"].ToString(),
                                Authorization: ctx.Request.Headers.Authorization.ToString());
                            await ctx.Response.WriteAsJsonAsync(echoed);
                        });
                    });
                })
                .StartAsync();

            HttpMessageHandler echoHandler = echoHost.GetTestServer().CreateHandler();

            webFactory = new GatewayWebApplicationFactory(echoHandler);

            // Eagerly trigger gateway startup so any mis-configuration surfaces here,
            // not inside individual test methods.
            _ = webFactory.Services;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (webFactory is not null)
            {
                await webFactory.DisposeAsync();
            }

            if (echoHost is not null)
            {
                await echoHost.StopAsync();
                echoHost.Dispose();
            }
        }
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="Program"/> that
    /// wires up the test doubles required to boot the gateway without real infrastructure.
    /// </summary>
    private sealed class GatewayWebApplicationFactory(HttpMessageHandler echoHandler)
        : WebApplicationFactory<Program>
    {
        /// <inheritdoc/>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Force Development environment so the production guard
            // (Testing:UseMockAuthentication + IsProduction) never throws.
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Activates the intent flag (harmless in Development).
                    ["Testing:UseMockAuthentication"] = "true",

                    // Provide a syntactically valid URL so app.MapRemote does not throw.
                    // The customer gRPC is never called because ITenantDatabaseStrategyResolver
                    // is replaced with a fake below.
                    ["Services:CustomerApi:Url"] = "http://customer-test.invalid",
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // ── Authentication ────────────────────────────────────────────────
                // Replace Keycloak JWT validation with the test-only mock scheme so no
                // real Keycloak server is needed. PostConfigure runs last and overrides
                // whatever AddKeycloak set as the default scheme.
                services
                    .AddAuthentication(MockBearerAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, MockBearerAuthenticationHandler>(
                        MockBearerAuthenticationHandler.SchemeName,
                        configureOptions: null);

                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
                });

                // ── Token exchange ────────────────────────────────────────────────
                // Replace the real Keycloak token-exchange service with a fake that
                // returns a deterministic token so ExchangeTokenStep succeeds without
                // any network calls.
                services.Replace(ServiceDescriptor.Singleton<IServiceTokenExchangeService>(
                    new FakeServiceTokenExchangeService()));

                // ── Tenant DB-strategy resolver ───────────────────────────────────
                // Replace the real gRPC-backed resolver with a fake that always returns
                // "shared" so ResolveDbStrategyStep succeeds without a customer service.
                services.Replace(ServiceDescriptor.Singleton<ITenantDatabaseStrategyResolver>(
                    new FakeTenantDatabaseStrategyResolver()));

                // ── YARP upstream ─────────────────────────────────────────────────
                // Replace the YARP HTTP client factory with one that routes all forwards
                // through the in-memory echo TestServer handler.  The echo server reads
                // the forwarded headers and returns them as JSON so assertions can verify
                // exactly what the gateway sent upstream.
                services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(
                    new TestForwarderHttpClientFactory(echoHandler)));
            });
        }
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a deterministic exchanged token so the ExchangeTokenStep succeeds in tests.
    /// </summary>
    private sealed class FakeServiceTokenExchangeService : IServiceTokenExchangeService
    {
        internal const string ExchangedToken = "fake-exchanged-downstream-token";

        /// <inheritdoc/>
        public Task<ServiceTokenResult> ExchangeTokenAsync(
            string subjectToken,
            string audience,
            string contextKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ServiceTokenResult(ExchangedToken, DateTime.UtcNow.AddHours(1)));
        }
    }

    /// <summary>
    /// Returns a successful "shared" DB strategy so the ResolveDbStrategyStep succeeds
    /// without calling the customer gRPC service.
    /// </summary>
    private sealed class FakeTenantDatabaseStrategyResolver : ITenantDatabaseStrategyResolver
    {
        /// <inheritdoc/>
        public Task<TenantDbStrategyResult> ResolveAsync(
            string tenantId,
            string? serviceName,
            CancellationToken ct)
        {
            return Task.FromResult(TenantDbStrategyResult.Ok("shared"));
        }
    }

    /// <summary>
    /// YARP HTTP client factory that routes all proxy forwards through the in-memory echo
    /// <see cref="TestServer"/> handler instead of making real network connections.
    /// </summary>
    private sealed class TestForwarderHttpClientFactory(HttpMessageHandler handler)
        : IForwarderHttpClientFactory
    {
        /// <inheritdoc/>
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
        {
            return new HttpMessageInvoker(handler, disposeHandler: false);
        }
    }
}

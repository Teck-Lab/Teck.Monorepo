// <copyright file="BasketCheckoutTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Baskets.Application.Baskets.Responses;
using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Baskets.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class BasketCheckoutTests : BasketIntegrationTestBase
{
    public BasketCheckoutTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Checkout_AfterAddingItem_ReturnsCreatedAndClearsActiveBasket()
    {
        var productId = Guid.NewGuid();

        var currentResponse = await Client.GetAsync("/baskets/current");
        var currentBody = await currentResponse.Content.ReadAsStringAsync();
        Assert.True(currentResponse.IsSuccessStatusCode, $"GET /baskets/current failed: {(int)currentResponse.StatusCode} {currentBody}");
        var current = await currentResponse.Content.ReadFromJsonAsync<BasketDto>();

        Assert.NotNull(current);
        Assert.NotEqual(Guid.Empty, current!.Id);

        var afterAdd = await Client.PostAsJsonAsync(
            "/baskets/items",
            new
            {
                BasketId = current.Id,
                ProductId = productId,
                ProductName = "Widget",
                UnitPrice = 10m,
                Quantity = 2,
            });

        afterAdd.EnsureSuccessStatusCode();

        // Watch-item: BasketItems.ProductId is ValueGeneratedOnAdd in the EF model (composite owned
        // key). Reload the basket from real Postgres (a fresh GET, not the in-memory handler result)
        // to prove the caller-supplied ProductId round-trips unchanged rather than being overwritten
        // by EF's Guid value generator.
        var reloaded = await Client.GetFromJsonAsync<BasketDto>("/baskets/current");
        Assert.NotNull(reloaded);
        Assert.Equal(current.Id, reloaded!.Id);
        var persistedItem = Assert.Single(reloaded.Items);
        Assert.Equal(productId, persistedItem.ProductId);
        Assert.Equal(2, persistedItem.Quantity);

        var checkout = await Client.PostAsJsonAsync("/baskets/checkout", new { BasketId = current.Id });

        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);

        var checkedOutBasket = await checkout.Content.ReadFromJsonAsync<BasketDto>();

        Assert.NotNull(checkedOutBasket);
        Assert.Equal(current.Id, checkedOutBasket!.Id);
        Assert.Equal("CheckedOut", checkedOutBasket.Status);

        // The checked-out basket is no longer "Active", so GetOrCreateBasketHandler must mint a new,
        // empty active basket for the same customer on the next call — proving checkout mutated
        // persisted state rather than just returning 201.
        var newActiveBasket = await Client.GetFromJsonAsync<BasketDto>("/baskets/current");
        Assert.NotNull(newActiveBasket);
        Assert.NotEqual(current.Id, newActiveBasket!.Id);
        Assert.Empty(newActiveBasket.Items);
    }
}

/// <summary>
/// Shared fixture base for basket integration tests: boots Basket.Host in-memory
/// via <see cref="WebApplicationFactory{TEntryPoint}"/> against a Testcontainers-backed Postgres
/// database, and replaces Keycloak JWT auth with a mock handler that always authenticates the
/// request as <see cref="MockBearerAuthenticationHandler.TestCustomerId"/>.
/// </summary>
public abstract class BasketIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    protected BasketIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        // Migrations live in Basket.Host (migrationsAssembly: typeof(Program).Assembly in AddBasketPersistence).
        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(
                typeof(Baskets.Application.Database.BasketDbContext),
                "Basket.Host")
            .GetAwaiter()
            .GetResult();

        factory = new BasketWebApplicationFactory(fixture, databaseConnectionString);
        Client = factory.CreateClient();
    }

    protected HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
    }

    private sealed class BasketWebApplicationFactory(
        SharedTestcontainersFixture fixture,
        string databaseConnectionString) : WebApplicationFactory<Program>
    {
        static BasketWebApplicationFactory()
        {
            // Basket.Host/Program.cs runs the host via RunTeckServiceAsync, which wraps JasperFx
            // command execution so the `codegen write` command works in container builds. When
            // WebApplicationFactory invokes that entry point with no command, the JasperFx command
            // runner would return an exit code instead of starting the in-memory server.
            // AutoStartHost tells JasperFx to start the host normally in that case, which is
            // exactly what WebApplicationFactory needs.
            JasperFxEnvironment.AutoStartHost = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // UseSetting applies at the highest configuration priority and overrides appsettings
            // connection strings that AddBasketPersistence reads during Program.cs setup.
            builder.UseSetting("ConnectionStrings:BasketWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:BasketRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            // Minimal Keycloak stubs so the production binding of KeycloakAuthenticationOptions
            // does not throw at startup. Actual JWT validation is replaced by MockBearerAuthenticationHandler.
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "basket-api");

            builder.ConfigureTestServices(services =>
            {
                // Register Finbuckle multi-tenant infrastructure so IMultiTenantContextAccessor<TenantDetails>
                // is available. No strategy or store is configured, so MultiTenantContext will be null per
                // request and the DbContext factories will fall back to the default connection string.
                services.AddMultiTenant<TenantDetails>();

                // Handler discovery for the Basket.Application assembly is configured in
                // Basket.Host/Program.cs (opts.Discovery.IncludeAssembly), so it applies here too —
                // the test boots the real host via WebApplicationFactory and needs no test-only
                // discovery wiring.

                // Replace the Keycloak JWT bearer handler with the test-only mock so that
                // AuthSchemes(JwtBearerDefaults.AuthenticationScheme) in AuthenticatedEndpoint
                // (used by CheckoutBasketEndpoint, which is not anonymous) resolves this handler
                // instead of attempting real JWT validation. The mock always injects a customer_id
                // claim, so GetOrCreateBasketHandler/CheckoutHandler resolve an authenticated
                // customer-owned basket even on the "anonymous" endpoints (AllowAnonymous only
                // skips authorization, not authentication — the middleware still runs and
                // populates HttpContext.User for every request).
                //
                // AddKeycloak in Basket.Host/Program.cs already registers "Bearer" as JwtBearerHandler.
                // Calling AddScheme("Bearer") again would throw "Scheme already exists: Bearer".
                // Instead, use PostConfigure<AuthenticationOptions> to replace the HandlerType of
                // the existing "Bearer" scheme builder and register our mock handler in DI.
                services.AddTransient<MockBearerAuthenticationHandler>();
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    var bearerScheme = options.Schemes
                        .FirstOrDefault(s => s.Name == MockBearerAuthenticationHandler.SchemeName);
                    if (bearerScheme is not null)
                    {
                        bearerScheme.HandlerType = typeof(MockBearerAuthenticationHandler);
                    }

                    options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
                });

                // Keycloak.AuthServices registers ParameterizedProtectedResourceRequirementHandler which
                // makes HTTP calls to the Keycloak authorization server (localhost:8080) to evaluate
                // protected resource requirements (required by CheckoutBasketEndpoint's permission).
                // Remove it and replace with a permissive test handler that succeeds the requirement
                // for any authenticated user without network calls.
                var keycloakHandlerDescriptor = services.FirstOrDefault(
                    d => d.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
                if (keycloakHandlerDescriptor is not null)
                {
                    services.Remove(keycloakHandlerDescriptor);
                }

                services.AddSingleton<IAuthorizationHandler, PermissiveProtectedResourceHandler>();
            });
        }
    }

    // Test-only authorization handler that bypasses Keycloak's ProtectedResourceRequirement
    // for any authenticated user. Registered only via ConfigureTestServices — never in production.
    private sealed class PermissiveProtectedResourceHandler
        : AuthorizationHandler<ParameterizedProtectedResourceRequirement>
    {
        /// <inheritdoc/>
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ParameterizedProtectedResourceRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

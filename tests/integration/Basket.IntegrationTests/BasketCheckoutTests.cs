// <copyright file="BasketCheckoutTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Baskets.Application.Baskets.EventHandlers.IntegrationEvents;
using Baskets.Application.Baskets.Features.Checkout.V1;
using Baskets.Application.Database;
using Baskets.Host.Database;
using Baskets.Application.Baskets.Responses;
using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.FeatureFlags;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Wolverine.Tracking;
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
    public async Task Checkout_SignedTenantClaimsOverrideCallerHeader_OnCommandAndRoutedEventEnvelopes()
    {
        Services.WolverineStubs(stubs => stubs.Stub<BasketCheckoutRequestedIntegrationEvent>(
            (_, _, _, _) => Task.CompletedTask));

        BasketDto basket = await Client.GetFromJsonAsync<BasketDto>("/baskets/current")
            ?? throw new InvalidOperationException("GET /baskets/current returned no basket.");

        HttpResponseMessage add = await Client.PostAsJsonAsync(
            "/baskets/items",
            new
            {
                BasketId = basket.Id,
                ProductId = Guid.NewGuid(),
                ProductName = "Widget",
                Quantity = 1,
            });
        add.EnsureSuccessStatusCode();

        using var checkoutRequest = new HttpRequestMessage(HttpMethod.Post, "/baskets/checkout")
        {
            Content = JsonContent.Create(new
            {
                BasketId = basket.Id,
                AuthorizedAmount = 20m,
                Currency = "USD",
                PaymentReference = "tok_tenant_envelope",
            }),
        };
        checkoutRequest.Headers.Add("X-TenantId", "caller-controlled-tenant");

        HttpResponseMessage? checkout = null;
        Func<IMessageContext, Task> sendCheckout = async _ =>
        {
            checkout = await Client.SendAsync(checkoutRequest).ConfigureAwait(false);
        };
        ITrackedSession tracking = await Services.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .ExecuteAndWaitAsync(sendCheckout)
            .ConfigureAwait(false);

        Assert.NotNull(checkout);
        Assert.Equal(HttpStatusCode.Accepted, checkout!.StatusCode);

        Envelope command = tracking.Executed.SingleEnvelope<CheckoutCommand>();
        Envelope integrationEvent = tracking.Sent.SingleEnvelope<BasketCheckoutRequestedIntegrationEvent>();

        Assert.Equal(MockBearerAuthenticationHandler.TestTenantId, command.TenantId);
        Assert.Equal(MockBearerAuthenticationHandler.TestTenantId, integrationEvent.TenantId);
        Assert.Equal(
            MockBearerAuthenticationHandler.TestTenantId,
            integrationEvent.Headers["X-TenantId"]);
    }

    [Fact]
    public async Task Checkout_AfterAddingItem_TransitionsToPricingPendingWithoutCallerPrice()
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

        var checkout = await Client.PostAsJsonAsync("/baskets/checkout", new
        {
            BasketId = current.Id,
            AuthorizedAmount = 20m,
            Currency = "USD",
            PaymentReference = "tok_checkout_integration",
        });

        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);

        var pricingPendingBasket = await checkout.Content.ReadFromJsonAsync<BasketDto>();

        Assert.NotNull(pricingPendingBasket);
        Assert.Equal(current.Id, pricingPendingBasket!.Id);
        Assert.Equal("PricingPending", pricingPendingBasket.Status);
        Assert.Equal(0m, pricingPendingBasket.Subtotal);

        // A pricing-pending basket is not an active basket, so a subsequent current-basket lookup
        // mints a separate active aggregate for the same authenticated Keycloak subject.
        var newActiveBasket = await Client.GetFromJsonAsync<BasketDto>("/baskets/current");
        Assert.NotNull(newActiveBasket);
        Assert.NotEqual(current.Id, newActiveBasket!.Id);
        Assert.Empty(newActiveBasket.Items);

        // Deliver the authoritative Pricing result after the real checkout request. The basket host
        // is intentionally isolated from Pricing.Host in this suite, so this is the consumer-side
        // delivery boundary rather than a fabricated pre-checkout state transition.
        await using var write = new BasketDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BasketDbContext>()
                .UseNpgsql(DatabaseConnectionString)
                .UseTeckCloudTenant(MockBearerAuthenticationHandler.TestTenantId)
                .Options,
            null!);
        var pending = await write.Baskets.Include(basket => basket.Items).SingleAsync(basket => basket.Id == current.Id);
        using var messageScope = Services.CreateScope();
        using var unitOfWork = new UnitOfWork<BasketDbContext>(write);
        await BasketPricedHandler.Handle(
            new BasketPricedIntegrationEvent
            {
                BasketId = current.Id,
                TenantId = pending.TenantId,
                RequestId = pending.CheckoutRequestId!,
                SourceCorrelationId = pending.CheckoutRequestId!,
                Amount = 15m,
                AuthorizedAmount = 20m,
                Currency = "USD",
                Lines = [new BasketPricedLine { ProductId = productId, UnitPrice = 7.50m, Quantity = 2, LineTotal = 15m }],
            },
            new BasketWriteRepository<Baskets.Domain.Entities.Basket, Guid>(write, new HttpContextAccessor()),
            unitOfWork,
            messageScope.ServiceProvider.GetRequiredService<IFeatureProvider>(),
            messageScope.ServiceProvider.GetRequiredService<IMessageBus>(),
            CancellationToken.None);

        Assert.Equal("CheckedOut", pending.Status.ToString());
        Assert.Equal(15m, pending.Subtotal);
        Assert.Equal(7.50m, Assert.Single(pending.Items).UnitPrice);
    }
}

/// <summary>
/// Shared fixture base for basket integration tests: boots Basket.Host in-memory
/// via <see cref="WebApplicationFactory{TEntryPoint}"/> against a Testcontainers-backed Postgres
/// database, and replaces Keycloak JWT auth with a mock handler that always authenticates the
/// request as <see cref="MockBearerAuthenticationHandler.TestSubject"/>.
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

    /// <summary>Gets the PostgreSQL connection string shared by this test project.</summary>
    protected string DatabaseConnectionString => databaseConnectionString;

    /// <summary>Gets the running basket host service provider.</summary>
    protected IServiceProvider Services => factory.Services;

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
            // Run as Development so AddTeckMessaging uses Wolverine's dynamic runtime codegen
            // (Static mode would require pre-generated handler code, which tests do not produce)
            // and creates the `wolverine` message-store schema on startup (no migrate init
            // container runs in tests). See WolverinePersistenceConfigurator.ConfigureCoreRuntime.
            builder.UseEnvironment("Development");

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
                // instead of attempting real JWT validation. The mock always injects a standard sub
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

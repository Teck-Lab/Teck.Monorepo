// <copyright file="InventoryStockTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net.Http.Json;
using Finbuckle.MultiTenant.Extensions;
using Inventories.Application.Database;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Inventories.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class InventoryStockTests : InventoryIntegrationTestBase
{
    public InventoryStockTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task RegisterStockItem_ThenAdjust_UpdatesAvailabilityAgainstRealPostgres()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var registerResponse = await Client.PostAsJsonAsync(
            "/inventory/stock-items",
            new
            {
                ProductId = productId,
                LocationId = locationId,
                QuantityOnHand = 5,
                AllowBackorder = false,
                ReorderThreshold = 0,
            });

        var registerBody = await registerResponse.Content.ReadAsStringAsync();
        Assert.True(registerResponse.IsSuccessStatusCode, $"POST /inventory/stock-items failed: {(int)registerResponse.StatusCode} {registerBody}");

        var registered = await registerResponse.Content.ReadFromJsonAsync<StockItemDto>();
        Assert.NotNull(registered);
        Assert.NotEqual(Guid.Empty, registered!.Id);
        Assert.Equal(productId, registered.ProductId);
        Assert.Equal(locationId, registered.LocationId);
        Assert.Equal(5, registered.OnHand);
        Assert.Equal(5, registered.Available);

        var afterRegister = await Client.GetFromJsonAsync<AvailabilityDto>($"/inventory/availability?productId={productId}");
        Assert.NotNull(afterRegister);
        Assert.Equal(productId, afterRegister!.ProductId);
        Assert.Equal(5, afterRegister.Available);

        var adjustResponse = await Client.PostAsJsonAsync(
            $"/inventory/stock-items/{registered.Id}/adjust",
            new { Id = registered.Id, Delta = -5 });

        var adjustBody = await adjustResponse.Content.ReadAsStringAsync();
        Assert.True(adjustResponse.IsSuccessStatusCode, $"POST /inventory/stock-items/{registered.Id}/adjust failed: {(int)adjustResponse.StatusCode} {adjustBody}");

        var adjusted = await adjustResponse.Content.ReadFromJsonAsync<StockItemDto>();
        Assert.NotNull(adjusted);
        Assert.Equal(registered.Id, adjusted!.Id);
        Assert.Equal(0, adjusted.OnHand);
        Assert.Equal(0, adjusted.Available);

        var afterAdjust = await Client.GetFromJsonAsync<AvailabilityDto>($"/inventory/availability?productId={productId}");
        Assert.NotNull(afterAdjust);
        Assert.Equal(productId, afterAdjust!.ProductId);
        Assert.Equal(0, afterAdjust.Available);
    }

    [Fact]
    public async Task GetAvailability_ForeignTenantStock_IsExcluded()
    {
        var productId = Guid.NewGuid();
        var foreignStock = StockItem.Create(
            productId,
            Guid.NewGuid(),
            "tenant-b",
            quantityOnHand: 23,
            allowBackorder: false,
            reorderThreshold: 0);

        await using (var seed = new InventoryDbContext(
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseNpgsql(DatabaseConnectionString)
                .UseTeckCloudTenant("tenant-b")
                .Options,
            null!))
        {
            seed.StockItems.Add(foreignStock);
            await seed.SaveChangesAsync();
        }

        AvailabilityDto availability = await Client.GetFromJsonAsync<AvailabilityDto>(
            $"/inventory/availability?productId={productId}")
            ?? throw new InvalidOperationException("GET /inventory/availability returned no availability.");

        Assert.Equal(productId, availability.ProductId);
        Assert.Equal(0, availability.Available);
        Assert.Empty(availability.ByLocation);
    }
}

/// <summary>
/// Shared fixture base for inventory integration tests: boots Inventory.Host in-memory
/// via <see cref="WebApplicationFactory{TEntryPoint}"/> against a Testcontainers-backed Postgres
/// database, and replaces Keycloak JWT auth with a mock handler that always authenticates the
/// request with a synthetic tenant claim.
/// </summary>
public abstract class InventoryIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    protected InventoryIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        // Migrations live in Inventory.Host (migrationsAssembly: typeof(Program).Assembly in AddInventoryPersistence).
        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(
                typeof(Inventories.Application.Database.InventoryDbContext),
                "Inventory.Host")
            .GetAwaiter()
            .GetResult();

        factory = new InventoryWebApplicationFactory(fixture, databaseConnectionString);
        Client = factory.CreateClient();
    }

    protected HttpClient Client { get; }

    /// <summary>Gets the PostgreSQL connection string shared by this test project.</summary>
    protected string DatabaseConnectionString => databaseConnectionString;

    /// <summary>
    /// Gets the host's root service provider, exposed so subclasses can open independent
    /// <see cref="IServiceScope"/>s (e.g. to resolve one <see cref="Wolverine.IMessageBus"/> per
    /// concurrent operation, giving each its own scoped DbContext).
    /// </summary>
    protected IServiceProvider Services => factory.Services;

    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
    }

    private sealed class InventoryWebApplicationFactory(
        SharedTestcontainersFixture fixture,
        string databaseConnectionString) : WebApplicationFactory<Program>
    {
        static InventoryWebApplicationFactory()
        {
            // Inventory.Host/Program.cs runs the host via RunTeckServiceAsync, which wraps JasperFx
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
            // connection strings that AddInventoryPersistence reads during Program.cs setup.
            builder.UseSetting("ConnectionStrings:InventoryWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:InventoryRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            // Minimal Keycloak stubs so the production binding of KeycloakAuthenticationOptions
            // does not throw at startup. Actual JWT validation is replaced by MockBearerAuthenticationHandler.
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "inventory-api");

            builder.ConfigureTestServices(services =>
            {
                // Handler discovery for the Inventory.Application assembly is configured in
                // Inventory.Host/Program.cs (opts.Discovery.IncludeAssembly), so it applies here too —
                // the test boots the real host via WebApplicationFactory and needs no test-only
                // discovery wiring.

                // Replace the Keycloak JWT bearer handler with the test-only mock so that
                // AuthSchemes(JwtBearerDefaults.AuthenticationScheme) in AuthenticatedEndpoint
                // (used by RegisterStockItemEndpoint/AdjustStockEndpoint/GetAvailabilityEndpoint,
                // none of which are anonymous) resolves this handler instead of attempting real JWT
                // validation. The mock always injects a tenant_id claim — inventory handlers resolve
                // the current tenant via ITenantInfo (Finbuckle), not from claims directly, but the
                // claim keeps requests authenticated so RequireProtectedResource has a principal to
                // evaluate.
                //
                // AddKeycloak in Inventory.Host/Program.cs already registers "Bearer" as JwtBearerHandler.
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
                // protected resource requirements (required by every inventory endpoint, none of which
                // are anonymous). Remove it and replace with a permissive test handler that succeeds
                // the requirement for any authenticated user without network calls.
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

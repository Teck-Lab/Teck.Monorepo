// <copyright file="OrderPlacedCrossServiceTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

extern alias OrderHost;
extern alias InventoryHost;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Finbuckle.MultiTenant.Extensions;
using Inventories.Application.Inventory.Responses;
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

namespace CrossService.IntegrationTests;

/// <summary>
/// Boots the order and inventory hosts against ONE shared RabbitMQ (and one shared Postgres),
/// then proves that placing an order on the order host causes the inventory host to reserve stock —
/// i.e. <c>OrderPlacedIntegrationEvent</c> genuinely travels producer → RabbitMQ → consumer.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class OrderPlacedCrossServiceTests : IDisposable
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly SharedTestcontainersFixture fixture;
    private readonly string orderConnectionString;
    private readonly string inventoryConnectionString;
    private readonly WebApplicationFactory<OrderHost::Program> orderFactory;
    private readonly WebApplicationFactory<InventoryHost::Program> inventoryFactory;
    private readonly HttpClient orderClient;
    private readonly HttpClient inventoryClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlacedCrossServiceTests"/> class,
    /// booting both hosts with the shared RabbitMQ connection so the transport is attached.
    /// </summary>
    /// <param name="fixture">The shared Testcontainers fixture (one Postgres + one RabbitMQ).</param>
    public OrderPlacedCrossServiceTests(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        orderConnectionString = fixture
            .CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host")
            .GetAwaiter()
            .GetResult();
        inventoryConnectionString = fixture
            .CreateSharedTestDatabaseAsync(typeof(Inventories.Application.Database.InventoryDbContext), "Inventory.Host")
            .GetAwaiter()
            .GetResult();

        inventoryFactory = new InventoryCrossServiceFactory(inventoryConnectionString, fixture.RabbitMqConnectionString);
        orderFactory = new OrderCrossServiceFactory(orderConnectionString, fixture.RabbitMqConnectionString);

        inventoryClient = inventoryFactory.CreateClient();
        orderClient = orderFactory.CreateClient();
    }

    /// <summary>
    /// Registers stock on inventory, places an order on order, and asserts that inventory's
    /// available quantity drops from 5 to 3 once the cross-service event is consumed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PlacingOrder_ReservesStockInInventory_AcrossRabbitMq()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        // Register 5 units of stock on the inventory host.
        var registerResponse = await inventoryClient.PostAsJsonAsync(
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

        var initialAvailability = await inventoryClient.GetFromJsonAsync<AvailabilityDto>(
            $"/inventory/availability?productId={productId}");
        Assert.NotNull(initialAvailability);
        Assert.Equal(5, initialAvailability!.Available);

        // Place an order for 2 units of the same product on the order host.
        var orderResponse = await orderClient.PostAsJsonAsync(
            "/orders",
            new
            {
                customerId = Guid.NewGuid(),
                lines = new[]
                {
                    new
                    {
                        productId,
                        productName = "Cross-service Widget",
                        quantity = 2,
                        unitPrice = 9.99m,
                    },
                },
            });

        var orderBody = await orderResponse.Content.ReadAsStringAsync();
        Assert.True(
            orderResponse.StatusCode == HttpStatusCode.Created,
            $"POST /orders expected 201 but got {(int)orderResponse.StatusCode}: {orderBody}");

        // Cross-process delivery is asynchronous: poll availability until the reservation commits.
        int? lastAvailable = await PollForAvailabilityAsync(productId, expected: 3);

        Assert.True(
            lastAvailable == 3,
            $"Expected inventory Available to reach 3 within {DeliveryTimeout.TotalSeconds:F0}s after the order " +
            $"was placed (proving OrderPlacedIntegrationEvent crossed RabbitMQ), but last observed Available was " +
            $"{(lastAvailable.HasValue ? lastAvailable.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<null>")}.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        orderClient.Dispose();
        inventoryClient.Dispose();
        orderFactory.Dispose();
        inventoryFactory.Dispose();
        fixture.TruncateAllTablesAsync(orderConnectionString).GetAwaiter().GetResult();
        fixture.TruncateAllTablesAsync(inventoryConnectionString).GetAwaiter().GetResult();
    }

    private async Task<int?> PollForAvailabilityAsync(Guid productId, int expected)
    {
        var stopwatch = Stopwatch.StartNew();
        int? lastAvailable = null;

        while (stopwatch.Elapsed < DeliveryTimeout)
        {
            var availability = await inventoryClient.GetFromJsonAsync<AvailabilityDto>(
                $"/inventory/availability?productId={productId}");
            lastAvailable = availability?.Available;

            if (lastAvailable == expected)
            {
                return lastAvailable;
            }

            await Task.Delay(PollInterval);
        }

        return lastAvailable;
    }

    /// <summary>
    /// Applies the shared test-only configuration (multi-tenancy scaffolding plus the mock bearer
    /// auth scheme and permissive Keycloak resource handler) common to both host factories.
    /// </summary>
    private static void ConfigureCommonTestServices(IServiceCollection services)
    {
        services.AddMultiTenant<TenantDetails>();

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

        var keycloakHandlerDescriptor = services.FirstOrDefault(
            d => d.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
        if (keycloakHandlerDescriptor is not null)
        {
            services.Remove(keycloakHandlerDescriptor);
        }

        services.AddSingleton<IAuthorizationHandler, PermissiveProtectedResourceHandler>();
    }

    private sealed class OrderCrossServiceFactory(string databaseConnectionString, string rabbitConnectionString)
        : WebApplicationFactory<OrderHost::Program>
    {
        static OrderCrossServiceFactory()
        {
            JasperFxEnvironment.AutoStartHost = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:OrderWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:OrderRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "order-api");

            builder.ConfigureTestServices(ConfigureCommonTestServices);
        }
    }

    private sealed class InventoryCrossServiceFactory(string databaseConnectionString, string rabbitConnectionString)
        : WebApplicationFactory<InventoryHost::Program>
    {
        static InventoryCrossServiceFactory()
        {
            JasperFxEnvironment.AutoStartHost = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:InventoryWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:InventoryRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "inventory-api");

            builder.ConfigureTestServices(ConfigureCommonTestServices);
        }
    }

    // Test-only authorization handler that bypasses Keycloak's ProtectedResourceRequirement
    // for any authenticated user. Registered only via ConfigureTestServices — never in production.
    private sealed class PermissiveProtectedResourceHandler
        : AuthorizationHandler<ParameterizedProtectedResourceRequirement>
    {
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

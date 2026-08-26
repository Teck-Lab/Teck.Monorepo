// <copyright file="ProductionLifecycleHarness.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

extern alias BasketHost;
extern alias BillingHost;
extern alias CatalogHost;
extern alias CustomerHost;
extern alias InventoryHost;
extern alias NotificationHost;
extern alias OrderHost;
extern alias PricingHost;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Baskets.Application.Database;
using Baskets.Domain.Entities;
using Catalog.Application.Products.Responses;
using Customers.Application.Database;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using Inventories.Application.Inventory.Responses;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Notifications.Application.Database;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using Orders.Application.Database;
using Orders.Domain.Entities;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;
using Wolverine.Tracking;

namespace CrossService.IntegrationTests;

/// <summary>
/// Bounded production-host fixture for the lifecycle proof.  It deliberately starts the actual
/// service entry points on one Testcontainers PostgreSQL/RabbitMQ pair; test code uses HTTP only
/// for ingress and reads databases only to observe committed outcomes.
/// </summary>
internal sealed class ProductionLifecycleHarness : IDisposable
{
    internal const string TenantId = DeterministicBearerToken.TenantId;
    internal const string Subject = DeterministicBearerToken.Subject;
    private readonly SharedTestcontainersFixture fixture;
    private readonly Dictionary<string, string> databases = new(StringComparer.Ordinal);

    private readonly WebApplicationFactory<CatalogHost::Program> catalogFactory;
    private readonly WebApplicationFactory<BasketHost::Program> basketFactory;
    private readonly WebApplicationFactory<PricingHost::Program> pricingFactory;
    private readonly WebApplicationFactory<OrderHost::Program> orderFactory;
    private readonly WebApplicationFactory<InventoryHost::Program> inventoryFactory;
    private readonly WebApplicationFactory<BillingHost::Program> billingFactory;
    private readonly WebApplicationFactory<CustomerHost::Program> customerFactory;
    private readonly WebApplicationFactory<NotificationHost::Program> notificationFactory;

    internal ProductionLifecycleHarness(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;
        databases["catalog"] = CreateDatabase<Catalog.Application.Database.CatalogDbContext>("Catalog.Host");
        databases["basket"] = CreateDatabase<BasketDbContext>("Basket.Host");
        databases["pricing"] = CreateDatabase<Pricing.Application.Database.PricingDbContext>("Pricing.Host");
        databases["order"] = CreateDatabase<OrderDbContext>("Order.Host");
        databases["inventory"] = CreateDatabase<Inventories.Application.Database.InventoryDbContext>("Inventory.Host");
        databases["billing"] = CreateDatabase<Billings.Application.Database.BillingDbContext>("Billing.Host");
        databases["customer"] = CreateDatabase<CustomerDbContext>("Customer.Host");
        databases["notification"] = CreateDatabase<NotificationDbContext>("Notification.Host");

        catalogFactory = new CatalogFactory(databases["catalog"], fixture.RabbitMqConnectionString);
        basketFactory = new BasketFactory(databases["basket"], fixture.RabbitMqConnectionString);
        pricingFactory = new PricingFactory(databases["pricing"], fixture.RabbitMqConnectionString);
        orderFactory = new OrderFactory(databases["order"], fixture.RabbitMqConnectionString);
        inventoryFactory = new InventoryFactory(databases["inventory"], fixture.RabbitMqConnectionString);
        billingFactory = new BillingFactory(databases["billing"], fixture.RabbitMqConnectionString);
        customerFactory = new CustomerFactory(databases["customer"], fixture.RabbitMqConnectionString);
        notificationFactory = new NotificationFactory(databases["notification"], fixture.RabbitMqConnectionString);

        Catalog = CreateClient(catalogFactory, "catalog-api");
        Basket = CreateClient(basketFactory, "basket-api");
        _ = pricingFactory.CreateClient();
        Inventory = CreateClient(inventoryFactory, "inventory-api");
        Order = CreateClient(orderFactory, "order-api");
        Billing = CreateClient(billingFactory, "billing-api");
        Customer = CreateClient(customerFactory, "customer-api");
        _ = notificationFactory.CreateClient();
    }

    internal HttpClient Catalog { get; }

    internal HttpClient Basket { get; }

    internal HttpClient Inventory { get; }

    internal HttpClient Customer { get; }

    internal HttpClient Order { get; }

    internal HttpClient Billing { get; }

    /// <summary>Creates a Basket client with a signed claim tenant that may differ from tenant selection.</summary>
    internal HttpClient CreateBasketClient(string? claimedTenantId = null, string? subject = null, bool includeBearer = true)
    {
        var client = basketFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-TenantId", TenantId);
        if (includeBearer)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DeterministicBearerToken.Issue("basket-api", subject ?? Subject, claimedTenantId ?? TenantId));
        }

        return client;
    }

    internal HttpClient CreateOrderClient(string? claimedTenantId = null, string? subject = null, bool includeBearer = true)
    {
        var client = orderFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-TenantId", TenantId);
        if (includeBearer)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DeterministicBearerToken.Issue("order-api", subject, claimedTenantId ?? TenantId));
        }

        return client;
    }

    /// <summary>Seeds the customer process's persisted source of truth before notification reconciliation.</summary>
    internal async Task SeedCustomerAsync()
    {
        var response = await Customer.PostAsJsonAsync("/customers", new
        {
            Email = "shopper@example.test",
            FirstName = "Shopper",
            LastName = "Test",
        });
        response.EnsureSuccessStatusCode();
    }

    internal async Task<ProductDto> CreateProductAsync(decimal sellPrice)
    {
        var response = await Catalog.PostAsJsonAsync("/products", new
        {
            Name = "Lifecycle widget",
            Description = (string?)null,
            CategoryId = (Guid?)null,
            Sku = $"LC-{Guid.NewGuid():N}",
            SellPriceAmount = sellPrice,
            SellPriceCurrency = "USD",
        });
        response.EnsureSuccessStatusCode();
        return Assert.IsType<ProductDto>(await response.Content.ReadFromJsonAsync<ProductDto>());
    }

    internal async Task<Inventories.Application.Inventory.Responses.StockItemDto> RegisterStockAsync(Guid productId, int quantity, bool allowBackorder = false)
    {
        var response = await Inventory.PostAsJsonAsync("/inventory/stock-items", new
        {
            ProductId = productId,
            LocationId = Guid.NewGuid(),
            QuantityOnHand = quantity,
            AllowBackorder = allowBackorder,
            ReorderThreshold = 0,
        });
        response.EnsureSuccessStatusCode();
        return Assert.IsType<Inventories.Application.Inventory.Responses.StockItemDto>(await response.Content.ReadFromJsonAsync<Inventories.Application.Inventory.Responses.StockItemDto>());
    }

    internal async Task AdjustStockAsync(Guid stockItemId, int delta)
    {
        var response = await Inventory.PostAsJsonAsync($"/inventory/stock-items/{stockItemId}/adjust", new { Delta = delta });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Runs only supported production ingress: current basket, item add, and checkout.</summary>
    internal async Task<Guid> CheckoutAsync(Guid productId, decimal authorizedAmount) => await CheckoutAsync(Basket, productId, authorizedAmount);

    /// <summary>Runs supported basket ingress with the supplied authenticated client.</summary>
    internal async Task<Guid> CheckoutAsync(HttpClient basketClient, Guid productId, decimal authorizedAmount)
    {
        var checkout = await TryCheckoutAsync(basketClient, productId, authorizedAmount);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, checkout.StatusCode);
        return checkout.BasketId;
    }

    /// <summary>Runs basket checkout and returns the HTTP result for hostile-path assertions.</summary>
    internal async Task<(Guid BasketId, HttpStatusCode StatusCode)> TryCheckoutAsync(HttpClient basketClient, Guid productId, decimal authorizedAmount)
    {
        var current = await basketClient.GetFromJsonAsync<Baskets.Application.Baskets.Responses.BasketDto>("/baskets/current");
        Assert.NotNull(current);
        var add = await basketClient.PostAsJsonAsync("/baskets/items", new
        {
            BasketId = current!.Id,
            ProductId = productId,
            ProductName = "Lifecycle widget",
            Quantity = 2,
        });
        add.EnsureSuccessStatusCode();
        var checkout = await basketClient.PostAsJsonAsync("/baskets/checkout", new
        {
            BasketId = current.Id,
            AuthorizedAmount = authorizedAmount,
            Currency = "USD",
            PaymentReference = "pm_lifecycle_token",
        });
        return (current.Id, checkout.StatusCode);
    }

    /// <summary>Attempts authenticated basket ingress without treating a rejected claim as success.</summary>
    internal async Task<(Guid BasketId, HttpStatusCode StatusCode)> TryAddBasketItemAsync(HttpClient basketClient, Guid productId)
    {
        var current = await basketClient.GetFromJsonAsync<Baskets.Application.Baskets.Responses.BasketDto>("/baskets/current");
        Assert.NotNull(current);
        var add = await basketClient.PostAsJsonAsync("/baskets/items", new
        {
            BasketId = current!.Id,
            ProductId = productId,
            ProductName = "Lifecycle widget",
            Quantity = 2,
        });
        return (current.Id, add.StatusCode);
    }

    internal async Task<Order?> WaitForConfirmedOrderAsync(Guid basketId)
    {
        return await WaitForReferenceAsync(async () =>
        {
            await using var context = CreateOrderContext();
            return await context.Orders.SingleOrDefaultAsync(order => order.BasketId == basketId);
        }, order => order?.Status == Orders.Domain.Entities.OrderStatus.Confirmed);
    }

    internal async Task<Order?> WaitForOrderAsync(Guid basketId, TimeSpan timeout)
    {
        return await WaitForReferenceAsync(async () =>
        {
            await using var context = CreateOrderContext();
            return await context.Orders.SingleOrDefaultAsync(order => order.BasketId == basketId);
        }, order => order is not null, timeout);
    }

    internal async Task<NotificationDelivery?> WaitForSentNotificationAsync(Guid orderId)
    {
        return await WaitForReferenceAsync(async () =>
        {
            await using var context = CreateNotificationContext();
            return await context.NotificationDeliveries.SingleOrDefaultAsync(delivery => delivery.OrderId == orderId);
        }, delivery => delivery?.Status == DeliveryStatus.Sent);
    }

    internal async Task<CustomerContact?> WaitForCustomerContactAsync()
    {
        return await WaitForReferenceAsync(async () =>
        {
            await using var context = CreateNotificationContext();
            return await context.CustomerContacts
                .SingleOrDefaultAsync(contact => contact.KeycloakSubjectId == Subject);
        }, contact => contact?.Email == "shopper@example.test");
    }

    internal async Task<int?> AvailabilityAsync(Guid productId)
    {
        var availability = await Inventory.GetFromJsonAsync<AvailabilityDto>($"/inventory/availability?productId={productId}");
        return availability?.Available;
    }

    internal async Task<bool> WaitForReservedAvailabilityAsync(Guid productId)
    {
        var availability = await WaitForAvailabilityAsync(() => AvailabilityAsync(productId), value => value == 3);
        return availability == 3;
    }

    internal async Task<Payment?> WaitForCapturedPaymentAsync(Guid orderId)
    {
        return await WaitForReferenceAsync(async () =>
        {
            await using var context = CreateBillingContext();
            return await context.Payments.SingleOrDefaultAsync(payment => payment.OrderId == orderId);
        }, payment => payment?.Status == PaymentStatus.Captured);
    }

    internal async Task<StubEmailAcceptance?> WaitForStubAcceptanceAsync(string idempotencyKey)
    {
        return await WaitForReferenceAsync(async () =>
        {
            await using var context = CreateNotificationContext();
            return await context.StubEmailAcceptances.SingleOrDefaultAsync(acceptance => acceptance.IdempotencyKey == idempotencyKey);
        }, acceptance => acceptance is not null);
    }

    internal async Task<int> CountOrdersAsync()
    {
        await using var context = CreateOrderContext();
        return await context.Orders.CountAsync();
    }

    internal async Task<int> CountPaymentsAsync()
    {
        await using var context = CreateBillingContext();
        return await context.Payments.CountAsync();
    }

    internal async Task<int> CountNotificationDeliveriesAsync(Guid orderId)
    {
        await using var context = CreateNotificationContext();
        return await context.NotificationDeliveries.CountAsync(delivery => delivery.OrderId == orderId);
    }

    /// <summary>Replaces only the outgoing checkout subscriber with a deterministic Wolverine test transport.</summary>
    internal void StubBasketCheckoutRequest()
    {
        basketFactory.Services.WolverineStubs(stubs => stubs.Stub<BasketCheckoutRequestedIntegrationEvent>(
            (_, _, _, _) => Task.CompletedTask));
    }

    /// <summary>Tracks one Basket-host HTTP action through Wolverine's synchronous test transport.</summary>
    internal Task<ITrackedSession> TrackBasketActivityAsync(Func<Task> action)
    {
        return basketFactory.Services.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .ExecuteAndWaitAsync(_ => action());
    }

    public void Dispose()
    {
        Catalog.Dispose();
        Basket.Dispose();
        Order.Dispose();
        Billing.Dispose();
        Inventory.Dispose();
        Customer.Dispose();
        notificationFactory.Dispose();
        customerFactory.Dispose();
        billingFactory.Dispose();
        inventoryFactory.Dispose();
        orderFactory.Dispose();
        pricingFactory.Dispose();
        basketFactory.Dispose();
        catalogFactory.Dispose();
        foreach (string connectionString in databases.Values)
        {
            fixture.TruncateAllTablesAsync(connectionString).GetAwaiter().GetResult();
        }
    }

    private string CreateDatabase<TContext>(string migrationsAssembly) where TContext : DbContext => fixture
        .CreateSharedTestDatabaseAsync(typeof(TContext), migrationsAssembly)
        .GetAwaiter().GetResult();

    private OrderDbContext CreateOrderContext() => new(
        new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(databases["order"], options => options.MigrationsAssembly("Order.Host"))
            .UseTeckCloudTenant(TenantId)
            .Options,
        null!);

    private NotificationDbContext CreateNotificationContext() => new(
        new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(databases["notification"], options => options.MigrationsAssembly("Notification.Host"))
            .UseTeckCloudTenant(TenantId)
            .Options,
        null!);

    private BillingDbContext CreateBillingContext() => new(
        new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(databases["billing"], options => options.MigrationsAssembly("Billing.Host"))
            .UseTeckCloudTenant(TenantId)
            .Options,
        null!);

    private static async Task<T?> WaitForReferenceAsync<T>(Func<Task<T?>> read, Func<T?, bool> complete, TimeSpan? timeout = null) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        T? latest = default;
        while (stopwatch.Elapsed < (timeout ?? TimeSpan.FromSeconds(45)))
        {
            latest = await read();
            if (complete(latest))
            {
                return latest;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return latest;
    }

    private static async Task<int?> WaitForAvailabilityAsync(Func<Task<int?>> read, Func<int?, bool> complete)
    {
        var stopwatch = Stopwatch.StartNew();
        int? latest = null;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(45))
        {
            latest = await read();
            if (complete(latest))
            {
                return latest;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return latest;
    }

    private static HttpClient CreateClient<TProgram>(WebApplicationFactory<TProgram> factory, string audience) where TProgram : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-TenantId", TenantId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DeterministicBearerToken.Issue(audience));
        return client;
    }

    private static void ConfigureCommonTestServices(IServiceCollection services)
    {
        var tenants = services.AddMultiTenant<TenantDetails>()
            .WithDelegateStrategy(context => Task.FromResult<string?>((context as Microsoft.AspNetCore.Http.HttpContext)?.Request.Headers["X-TenantId"].ToString()));
        services.AddHttpContextAccessor();
        services.AddScoped<IMultiTenantStore<TenantDetails>, HeaderTenantStore>();
        tenants.WithStore<HeaderTenantStore>(ServiceLifetime.Scoped);
        services.AddSingleton<IStartupFilter, TestTenantResolutionStartupFilter>();
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.RequireHttpsMetadata = false;
            options.MetadataAddress = null;
            options.ConfigurationManager = null!;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = DeterministicBearerToken.SigningKey,
                ValidateIssuer = true,
                ValidIssuer = DeterministicBearerToken.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                RequireAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        });
        var keycloak = services.FirstOrDefault(service => service.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
        if (keycloak is not null)
        {
            services.Remove(keycloak);
        }

        services.AddSingleton<IAuthorizationHandler, PermissiveProtectedResourceHandler>();
    }

    private static void ConfigureHost(IWebHostBuilder builder, string connectionName, string connectionString, string rabbitConnectionString, string resource, bool v2 = true)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{connectionName}Write", connectionString);
        builder.UseSetting($"ConnectionStrings:{connectionName}Read", connectionString);
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);
        builder.UseSetting("Keycloak:realm", "test");
        builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
        builder.UseSetting("Keycloak:resource", resource);
        // Inventory reads the direct deployment setting and maps it into the provider; the other
        // production hosts bind FeatureFlagOptions.Flags directly. Configure both shapes so the
        // V2 event chain is enabled consistently across every host in this shared lifecycle seam.
        builder.UseSetting("FeatureFlags:CheckoutLifecycleV2", v2.ToString());
        builder.UseSetting("FeatureFlags:Flags:CheckoutLifecycleV2", v2.ToString());
        builder.ConfigureTestServices(ConfigureCommonTestServices);
    }

    private sealed class CatalogFactory(string connectionString, string rabbit) : WebApplicationFactory<CatalogHost::Program>
    {
        static CatalogFactory() => JasperFxEnvironment.AutoStartHost = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureHost(builder, "Catalog", connectionString, rabbit, "catalog-api");
    }

    private sealed class BasketFactory(string connectionString, string rabbit) : WebApplicationFactory<BasketHost::Program>
    {
        static BasketFactory() => JasperFxEnvironment.AutoStartHost = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureHost(builder, "Basket", connectionString, rabbit, "basket-api");
    }

    private sealed class PricingFactory(string connectionString, string rabbit) : WebApplicationFactory<PricingHost::Program>
    {
        static PricingFactory() => JasperFxEnvironment.AutoStartHost = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureHost(builder, "Pricing", connectionString, rabbit, "pricing-api");
    }

    private sealed class OrderFactory(string connectionString, string rabbit) : WebApplicationFactory<OrderHost::Program>
    {
        static OrderFactory() => JasperFxEnvironment.AutoStartHost = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureHost(builder, "Order", connectionString, rabbit, "order-api");
    }

    private sealed class InventoryFactory(string connectionString, string rabbit) : WebApplicationFactory<InventoryHost::Program>
    {
        static InventoryFactory() => JasperFxEnvironment.AutoStartHost = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureHost(builder, "Inventory", connectionString, rabbit, "inventory-api");
    }

    private sealed class BillingFactory(string connectionString, string rabbit) : WebApplicationFactory<BillingHost::Program>
    {
        static BillingFactory() => JasperFxEnvironment.AutoStartHost = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureHost(builder, "Billing", connectionString, rabbit, "billing-api");
    }

    private sealed class CustomerFactory(string connectionString, string rabbit) : WebApplicationFactory<CustomerHost::Program>
    {
        static CustomerFactory() => JasperFxEnvironment.AutoStartHost = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureHost(builder, "Customer", connectionString, rabbit, "customer-api");
    }

    private sealed class NotificationFactory(string connectionString, string rabbit) : WebApplicationFactory<NotificationHost::Program>
    {
        static NotificationFactory() => JasperFxEnvironment.AutoStartHost = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfigureHost(builder, "Notification", connectionString, rabbit, "notification-api");
    }

    private sealed class TestTenantResolutionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseMultiTenant();
            next(app);
        };
    }

    private sealed class PermissiveProtectedResourceHandler : AuthorizationHandler<ParameterizedProtectedResourceRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ParameterizedProtectedResourceRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

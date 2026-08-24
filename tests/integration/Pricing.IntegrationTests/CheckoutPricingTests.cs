// <copyright file="CheckoutPricingTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

extern alias BasketHost;
extern alias CatalogHost;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Baskets.Application.Baskets.Responses;
using Baskets.Application.Database;
using Catalog.Application.Products.Responses;
using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Application.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Pricing.IntegrationTests;

/// <summary>Exercises the real catalog-to-pricing-to-basket checkout path over RabbitMQ.</summary>
[Collection("SharedTestcontainers")]
public sealed class CheckoutPricingTests : IDisposable
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly SharedTestcontainersFixture fixture;
    private readonly string catalogConnectionString;
    private readonly string pricingConnectionString;
    private readonly string basketConnectionString;
    private readonly WebApplicationFactory<CatalogHost::Program> catalogFactory;
    private readonly WebApplicationFactory<global::Program> pricingFactory;
    private readonly WebApplicationFactory<BasketHost::Program> basketFactory;
    private readonly HttpClient catalogClient;
    private readonly HttpClient basketClient;

    /// <summary>Starts catalog before pricing, then boots the subscriber hosts on the shared broker.</summary>
    public CheckoutPricingTests(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;
        catalogConnectionString = fixture.CreateSharedTestDatabaseAsync(typeof(Catalog.Application.Database.CatalogDbContext), "Catalog.Host").GetAwaiter().GetResult();
        pricingConnectionString = fixture.CreateSharedTestDatabaseAsync(typeof(PricingDbContext), "Pricing.Host").GetAwaiter().GetResult();
        basketConnectionString = fixture.CreateSharedTestDatabaseAsync(typeof(BasketDbContext), "Basket.Host").GetAwaiter().GetResult();

        catalogFactory = new CatalogFactory(catalogConnectionString, fixture.RabbitMqConnectionString);
        catalogClient = catalogFactory.CreateClient();
        pricingFactory = new PricingFactory(pricingConnectionString, fixture.RabbitMqConnectionString);
        basketFactory = new BasketFactory(basketConnectionString, fixture.RabbitMqConnectionString);
        basketClient = basketFactory.CreateClient();
    }

    /// <summary>Proves a catalog product that predates the pricing subscriber completes checkout through reconciliation.</summary>
    [Fact]
    public async Task ProductPredatesPricingSubscriber_CheckoutCompletesAtCatalogSellPrice()
    {
        ProductDto product = await CreateProductAsync(12.50m);
        using HttpClient pricingClient = pricingFactory.CreateClient();

        BasketDto basket = await CheckoutAsync(product.Id, 30m);

        var completed = await PollForCompletedBasketAsync(basket.Id);
        Assert.NotNull(completed);
        Assert.Equal("CheckedOut", completed!.Status.ToString());
        Assert.Equal(25m, completed.Subtotal);
        Assert.Equal(12.50m, Assert.Single(completed.Items).UnitPrice);
    }

    /// <summary>Proves a sell-price change missed before pricing starts is recovered at its current catalog value.</summary>
    [Fact]
    public async Task PriceChangeBeforePricingSubscriber_CheckoutCompletesAtCurrentCatalogSellPrice()
    {
        ProductDto product = await CreateProductAsync(10m);
        var updated = await catalogClient.PutAsJsonAsync(
            $"/products/{product.Id}/variants/{product.Variants[0].Id}/sell-price",
            new { ProductId = product.Id, VariantId = product.Variants[0].Id, Amount = 20m, Currency = "USD" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using HttpClient pricingClient = pricingFactory.CreateClient();
        BasketDto basket = await CheckoutAsync(product.Id, 50m);

        var completed = await PollForCompletedBasketAsync(basket.Id);
        Assert.NotNull(completed);
        Assert.Equal("CheckedOut", completed!.Status.ToString());
        Assert.Equal(40m, completed.Subtotal);
        Assert.Equal(20m, Assert.Single(completed.Items).UnitPrice);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        basketClient.Dispose();
        basketFactory.Dispose();
        pricingFactory.Dispose();
        catalogClient.Dispose();
        catalogFactory.Dispose();
        fixture.TruncateAllTablesAsync(catalogConnectionString).GetAwaiter().GetResult();
        fixture.TruncateAllTablesAsync(pricingConnectionString).GetAwaiter().GetResult();
        fixture.TruncateAllTablesAsync(basketConnectionString).GetAwaiter().GetResult();
    }

    private async Task<ProductDto> CreateProductAsync(decimal amount)
    {
        var response = await catalogClient.PostAsJsonAsync("/products", new
        {
            Name = "Catalog fallback widget",
            Description = (string?)null,
            CategoryId = (Guid?)null,
            Sku = $"CF-{Guid.NewGuid():N}",
            SellPriceAmount = amount,
            SellPriceCurrency = "USD",
        });
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Catalog product creation failed: {(int)response.StatusCode} {body}");
        return Assert.IsType<ProductDto>(await response.Content.ReadFromJsonAsync<ProductDto>());
    }

    private async Task<BasketDto> CheckoutAsync(Guid productId, decimal authorizedAmount)
    {
        var current = await basketClient.GetFromJsonAsync<BasketDto>("/baskets/current");
        Assert.NotNull(current);
        var added = await basketClient.PostAsJsonAsync("/baskets/items", new
        {
            BasketId = current!.Id,
            ProductId = productId,
            ProductName = "Catalog fallback widget",
            Quantity = 2,
        });
        added.EnsureSuccessStatusCode();
        var checkout = await basketClient.PostAsJsonAsync("/baskets/checkout", new
        {
            BasketId = current.Id,
            AuthorizedAmount = authorizedAmount,
            Currency = "USD",
            PaymentReference = "tok_pricing_reconciliation",
        });
        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);
        return Assert.IsType<BasketDto>(await checkout.Content.ReadFromJsonAsync<BasketDto>());
    }

    private async Task<Baskets.Domain.Entities.Basket?> PollForCompletedBasketAsync(Guid basketId)
    {
        var stopwatch = Stopwatch.StartNew();
        Baskets.Domain.Entities.Basket? last = null;
        while (stopwatch.Elapsed < DeliveryTimeout)
        {
            await using var context = new BasketDbContext(
                new DbContextOptionsBuilder<BasketDbContext>()
                    .UseNpgsql(basketConnectionString)
                    .UseTeckCloudTenant(MockBearerAuthenticationHandler.TestTenantId)
                    .Options,
                null!);
            last = await context.Baskets.Include(basket => basket.Items).SingleOrDefaultAsync(basket => basket.Id == basketId);
            if (last?.Status.ToString() == "CheckedOut")
            {
                return last;
            }

            await Task.Delay(PollInterval);
        }

        return last;
    }

    private static void ConfigureCommonTestServices(IServiceCollection services)
    {
        services.AddMultiTenant<TenantDetails>();
        services.AddTransient<MockBearerAuthenticationHandler>();
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            var bearer = options.Schemes.FirstOrDefault(scheme => scheme.Name == MockBearerAuthenticationHandler.SchemeName);
            if (bearer is not null)
            {
                bearer.HandlerType = typeof(MockBearerAuthenticationHandler);
            }

            options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
            options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
            options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
        });

        var keycloakHandler = services.FirstOrDefault(descriptor => descriptor.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
        if (keycloakHandler is not null)
        {
            services.Remove(keycloakHandler);
        }

        services.AddSingleton<IAuthorizationHandler, PermissiveProtectedResourceHandler>();
    }

    private sealed class CatalogFactory(string connectionString, string rabbitConnectionString)
        : WebApplicationFactory<CatalogHost::Program>
    {
        static CatalogFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CatalogWrite", connectionString);
            builder.UseSetting("ConnectionStrings:CatalogRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);
            ConfigureHost(builder, "catalog-api");
        }
    }

    private sealed class PricingFactory(string connectionString, string rabbitConnectionString)
        : WebApplicationFactory<global::Program>
    {
        static PricingFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:PricingWrite", connectionString);
            builder.UseSetting("ConnectionStrings:PricingRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);
            ConfigureHost(builder, "pricing-api");
        }
    }

    private sealed class BasketFactory(string connectionString, string rabbitConnectionString)
        : WebApplicationFactory<BasketHost::Program>
    {
        static BasketFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:BasketWrite", connectionString);
            builder.UseSetting("ConnectionStrings:BasketRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);
            ConfigureHost(builder, "basket-api");
        }
    }

    private static void ConfigureHost(IWebHostBuilder builder, string resource)
    {
        builder.UseSetting("Keycloak:realm", "test");
        builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
        builder.UseSetting("Keycloak:resource", resource);
        builder.ConfigureTestServices(ConfigureCommonTestServices);
    }

    private sealed class PermissiveProtectedResourceHandler
        : AuthorizationHandler<ParameterizedProtectedResourceRequirement>
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

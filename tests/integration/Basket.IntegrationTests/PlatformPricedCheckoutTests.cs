// <copyright file="PlatformPricedCheckoutTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Baskets.Application.Baskets.EventHandlers.IntegrationEvents;
using Baskets.Application.Baskets.Responses;
using Baskets.Application.Database;
using Baskets.Domain.Entities;
using Baskets.Host.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.FeatureFlags;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace Baskets.IntegrationTests;

/// <summary>Exercises terminal authoritative-pricing checkout states against the real basket host and database.</summary>
[Collection("SharedTestcontainers")]
public sealed class PlatformPricedCheckoutTests : BasketIntegrationTestBase
{
    /// <summary>Initializes the test against the shared basket fixture.</summary>
    public PlatformPricedCheckoutTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>Moves a checkout from active through pending to checked out using only the pricing response values.</summary>
    [Fact]
    public async Task Checkout_AuthoritativePrice_TransitionsActiveToPendingToCheckedOut()
    {
        var (basket, productId) = await StartCheckoutAsync(authorizedAmount: 20m);
        string requestId = await CheckoutRequestIdAsync(basket.Id);

        await ApplyPricedAsync(basket.Id, productId, requestId, amount: 15m, currency: "USD", unitPrice: 7.50m);

        await using BasketDbContext context = CreateContext();
        Basket persisted = Assert.Single(await context.Baskets.Include(item => item.Items).Where(item => item.Id == basket.Id).ToListAsync());
        Assert.Equal("CheckedOut", persisted.Status.Name);
        Assert.Equal(15m, persisted.Subtotal);
        Assert.Equal(7.50m, Assert.Single(persisted.Items).UnitPrice);
    }

    /// <summary>Records a structured pricing failure as a terminal failed checkout without a checked-out basket.</summary>
    [Fact]
    public async Task Checkout_PricingFailure_TransitionsPendingToCheckoutFailed()
    {
        var (basket, _) = await StartCheckoutAsync(authorizedAmount: 20m);
        string requestId = await CheckoutRequestIdAsync(basket.Id);

        await HandleFailureAsync(new BasketPricingFailedIntegrationEvent
        {
            BasketId = basket.Id,
            TenantId = await TenantIdAsync(basket.Id),
            RequestId = requestId,
            SourceCorrelationId = basket.Id.ToString("N"),
            FailureCategory = "price-unavailable",
        });

        await AssertFailedWithoutCheckoutAsync(basket.Id);
    }

    /// <summary>Rejects a pricing response whose total exceeds the authorization ceiling.</summary>
    [Fact]
    public async Task Checkout_OverCeilingPrice_IsRejectedWithoutCheckout()
    {
        var (basket, productId) = await StartCheckoutAsync(authorizedAmount: 10m);
        string requestId = await CheckoutRequestIdAsync(basket.Id);

        await ApplyPricedAsync(basket.Id, productId, requestId, amount: 10.01m, currency: "USD", unitPrice: 10.01m);

        await AssertFailedWithoutCheckoutAsync(basket.Id);
    }

    /// <summary>Rejects an authoritative price reported in a currency other than the shopper authorization.</summary>
    [Fact]
    public async Task Checkout_CurrencyMismatch_IsRejectedWithoutCheckout()
    {
        var (basket, productId) = await StartCheckoutAsync(authorizedAmount: 20m);
        string requestId = await CheckoutRequestIdAsync(basket.Id);

        await ApplyPricedAsync(basket.Id, productId, requestId, amount: 15m, currency: "EUR", unitPrice: 7.50m);

        await AssertFailedWithoutCheckoutAsync(basket.Id);
    }

    /// <summary>Rejects an unidentified shopper visibly before a basket checkout or pricing request can be created.</summary>
    [Fact]
    public async Task Checkout_AnonymousShopper_ReturnsUnauthorizedWithoutCreatingCheckout()
    {
        Client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        var response = await Client.PostAsJsonAsync("/baskets/checkout", new
        {
            BasketId = Guid.NewGuid(),
            AuthorizedAmount = 20m,
            Currency = "USD",
            PaymentReference = "tok_anonymous_checkout",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using BasketDbContext context = CreateContext();
        Assert.Empty(await context.Baskets.ToListAsync());
    }

    private async Task<(BasketDto Basket, Guid ProductId)> StartCheckoutAsync(decimal authorizedAmount)
    {
        Guid productId = Guid.NewGuid();
        BasketDto basket = (await Client.GetFromJsonAsync<BasketDto>("/baskets/current"))!;

        var addItem = await Client.PostAsJsonAsync("/baskets/items", new
        {
            BasketId = basket.Id,
            ProductId = productId,
            ProductName = "Platform-priced widget",
            Quantity = 2,
            UnitPrice = 0.01m,
        });
        addItem.EnsureSuccessStatusCode();

        var checkout = await Client.PostAsJsonAsync("/baskets/checkout", new
        {
            BasketId = basket.Id,
            AuthorizedAmount = authorizedAmount,
            Currency = "USD",
            PaymentReference = "tok_platform_pricing",
        });
        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);
        BasketDto pending = (await checkout.Content.ReadFromJsonAsync<BasketDto>())!;
        Assert.Equal("PricingPending", pending.Status);
        Assert.Equal(0m, pending.Subtotal);
        return (pending, productId);
    }

    private async Task ApplyPricedAsync(Guid basketId, Guid productId, string requestId, decimal amount, string currency, decimal unitPrice)
    {
        using IServiceScope scope = Services.CreateScope();
        await using BasketDbContext context = CreateContext();
        using var unitOfWork = new UnitOfWork<BasketDbContext>(context);
        await BasketPricedHandler.Handle(
            new BasketPricedIntegrationEvent
            {
                BasketId = basketId,
                TenantId = await TenantIdAsync(basketId),
                Amount = amount,
                AuthorizedAmount = await AuthorizedAmountAsync(basketId),
                Currency = currency,
                RequestId = requestId,
                SourceCorrelationId = basketId.ToString("N"),
                Lines = [new BasketPricedLine { ProductId = productId, Quantity = 2, UnitPrice = unitPrice, LineTotal = amount }],
            },
            new BasketWriteRepository<Basket, Guid>(context, new HttpContextAccessor()),
            unitOfWork,
            scope.ServiceProvider.GetRequiredService<IFeatureProvider>(),
            scope.ServiceProvider.GetRequiredService<IMessageBus>(),
            CancellationToken.None);
    }

    private async Task HandleFailureAsync(BasketPricingFailedIntegrationEvent integrationEvent)
    {
        await using BasketDbContext context = CreateContext();
        using var unitOfWork = new UnitOfWork<BasketDbContext>(context);
        await BasketPricingFailedHandler.Handle(
            integrationEvent,
            new BasketWriteRepository<Basket, Guid>(context, new HttpContextAccessor()),
            unitOfWork,
            CancellationToken.None);
    }

    private async Task<string> CheckoutRequestIdAsync(Guid basketId)
    {
        await using BasketDbContext context = CreateContext();
        return (await context.Baskets.SingleAsync(basket => basket.Id == basketId)).CheckoutRequestId!;
    }

    private async Task<decimal> AuthorizedAmountAsync(Guid basketId)
    {
        await using BasketDbContext context = CreateContext();
        return (await context.Baskets.SingleAsync(basket => basket.Id == basketId)).AuthorizedAmount;
    }

    private async Task<string> TenantIdAsync(Guid basketId)
    {
        await using BasketDbContext context = CreateContext();
        return (await context.Baskets.SingleAsync(basket => basket.Id == basketId)).TenantId;
    }

    private async Task AssertFailedWithoutCheckoutAsync(Guid basketId)
    {
        await using BasketDbContext context = CreateContext();
        Basket persisted = await context.Baskets.SingleAsync(basket => basket.Id == basketId);
        Assert.Equal("CheckoutFailed", persisted.Status.Name);
        Assert.NotEqual("CheckedOut", persisted.Status.Name);
    }

    private BasketDbContext CreateContext() => new(
        new DbContextOptionsBuilder<BasketDbContext>()
            .UseNpgsql(DatabaseConnectionString)
            .UseTeckCloudTenant(MockBearerAuthenticationHandler.TestTenantId)
            .Options,
        null!);
}

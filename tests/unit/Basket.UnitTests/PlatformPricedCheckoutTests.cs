using Ardalis.Specification;
using Baskets.Application.Baskets.EventHandlers.IntegrationEvents;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.FeatureFlags;
using Wolverine;
using Xunit;

namespace Baskets.UnitTests;

public sealed class PlatformPricedCheckoutTests
{
    [Fact]
    public async Task Handle_AuthoritativePriceAtOrBelowCeiling_WhenV2IsDisabled_CompletesCheckoutWithoutV2Publication()
    {
        var basket = Basket.CreateForSubject("shopper-subject", "tenant-1");
        var productId = Guid.NewGuid();
        basket.AddItem(productId, "Widget", 2);
        basket.BeginCheckout(25m, "USD", "tok_test_123");
        var baskets = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        baskets.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var features = Substitute.For<IFeatureProvider>();
        features.IsEnabled("CheckoutLifecycleV2").Returns(false);
        var bus = Substitute.For<IMessageBus>();

        await BasketPricedHandler.Handle(new BasketPricedIntegrationEvent
        {
            BasketId = basket.Id,
            TenantId = "tenant-1",
            Amount = 20m,
            AuthorizedAmount = 25m,
            Currency = "USD",
            RequestId = basket.CheckoutRequestId!,
            Lines = [new BasketPricedLine { ProductId = productId, UnitPrice = 10m, Quantity = 2, LineTotal = 20m }],
        }, baskets, unitOfWork, features, bus, CancellationToken.None);

        Assert.Equal(BasketStatus.CheckedOut, basket.Status);
        Assert.Equal(20m, basket.Subtotal);
        Assert.Equal(10m, Assert.Single(basket.Items).UnitPrice);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<BasketCheckedOutV2IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_AuthoritativePriceAtOrBelowCeiling_WhenV2IsEnabled_PublishesOneV2Checkout()
    {
        var basket = Basket.CreateForSubject("shopper-subject", "tenant-1");
        var productId = Guid.NewGuid();
        basket.AddItem(productId, "Widget", 2);
        basket.BeginCheckout(25m, "USD", "tok_test_123");
        var baskets = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        baskets.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var features = Substitute.For<IFeatureProvider>();
        features.IsEnabled("CheckoutLifecycleV2").Returns(true);
        var bus = Substitute.For<IMessageBus>();

        await BasketPricedHandler.Handle(new BasketPricedIntegrationEvent
        {
            BasketId = basket.Id,
            TenantId = "tenant-1",
            Amount = 20m,
            AuthorizedAmount = 25m,
            Currency = "USD",
            RequestId = basket.CheckoutRequestId!,
            SourceCorrelationId = "authoritative-pricing",
            Lines = [new BasketPricedLine { ProductId = productId, UnitPrice = 10m, Quantity = 2, LineTotal = 20m }],
        }, baskets, unitOfWork, features, bus, CancellationToken.None);

        Assert.Equal(BasketStatus.CheckedOut, basket.Status);
        await bus.Received(1).PublishAsync(Arg.Is<BasketCheckedOutV2IntegrationEvent>(evt =>
            evt.BasketId == basket.Id &&
            evt.TenantId == basket.TenantId &&
            evt.Amount == 20m &&
            evt.AuthorizedAmount == 25m &&
            evt.SourceCorrelationId == "authoritative-pricing"));
    }

    [Fact]
    public async Task Handle_PriceAboveCeiling_FailsCheckoutAndPublishesNothing()
    {
        var basket = Basket.CreateForSubject("shopper-subject", "tenant-1");
        var productId = Guid.NewGuid();
        basket.AddItem(productId, "Widget", 1);
        basket.BeginCheckout(10m, "USD", "tok_test_123");
        var baskets = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        baskets.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var features = Substitute.For<IFeatureProvider>();
        var bus = Substitute.For<IMessageBus>();

        await BasketPricedHandler.Handle(new BasketPricedIntegrationEvent
        {
            BasketId = basket.Id,
            TenantId = "tenant-1",
            Amount = 11m,
            AuthorizedAmount = 10m,
            Currency = "USD",
            RequestId = basket.CheckoutRequestId!,
            Lines = [new BasketPricedLine { ProductId = productId, UnitPrice = 11m, Quantity = 1, LineTotal = 11m }],
        }, baskets, unitOfWork, features, bus, CancellationToken.None);

        Assert.Equal(BasketStatus.CheckoutFailed, basket.Status);
        await bus.DidNotReceive().PublishAsync(Arg.Any<BasketCheckedOutV2IntegrationEvent>());
    }
}

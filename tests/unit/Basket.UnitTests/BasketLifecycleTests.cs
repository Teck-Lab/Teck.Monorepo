using Baskets.Domain.DomainEvents;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketLifecycleTests
{
    [Fact]
    public void BeginCheckout_WithItems_SetsPricingPendingAndRaisesEvent()
    {
        var basket = Basket.CreateForSubject("shopper-subject", "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 2);

        basket.BeginCheckout(25m, "USD", "tok_test_123");

        Assert.Equal(BasketStatus.PricingPending, basket.Status);
        Assert.Contains(basket.DomainEvents, e => e is BasketCheckedOut);
    }

    [Fact]
    public void Checkout_EmptyBasket_Throws()
    {
        var basket = Basket.CreateForSubject("shopper-subject", "tenant-1");

        Assert.Throws<InvalidOperationException>(() => basket.BeginCheckout(25m, "USD", "tok_test_123"));
    }

    [Fact]
    public void MergeFrom_CombinesItemsAndMarksSourceMerged()
    {
        var shared = Guid.NewGuid();
        var target = Basket.CreateForSubject("owner-subject", "tenant-1");
        target.AddItem(shared, "Widget", 10m, 1);
        var source = Basket.CreateAnonymous(Guid.NewGuid(), "tenant-1");
        source.AddItem(shared, "Widget", 10m, 2);
        source.AddItem(Guid.NewGuid(), "Gadget", 5m, 1);

        target.MergeFrom(source);

        Assert.Equal(BasketStatus.Merged, source.Status);
        Assert.Equal(2, target.Items.Count);
        Assert.Equal(3, target.Items.First(i => i.ProductId == shared).Quantity);
    }

    [Fact]
    public void AssignToSubject_TransfersOwnership()
    {
        const string subject = "owner-subject";
        var basket = Basket.CreateAnonymous(Guid.NewGuid(), "tenant-1");

        basket.AssignToSubject(subject);

        Assert.Equal(subject, basket.Subject);
        Assert.Null(basket.AnonymousToken);
    }
}

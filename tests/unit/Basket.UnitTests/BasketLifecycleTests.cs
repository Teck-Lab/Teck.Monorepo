using Baskets.Domain.DomainEvents;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketLifecycleTests
{
    [Fact]
    public void Checkout_WithItems_SetsStatusAndRaisesEvent()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 10m, 2);

        basket.Checkout();

        Assert.Equal(BasketStatus.CheckedOut, basket.Status);
        Assert.Contains(basket.DomainEvents, e => e is BasketCheckedOut);
    }

    [Fact]
    public void Checkout_EmptyBasket_Throws()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");

        Assert.Throws<InvalidOperationException>(() => basket.Checkout());
    }

    [Fact]
    public void MergeFrom_CombinesItemsAndMarksSourceMerged()
    {
        var shared = Guid.NewGuid();
        var target = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
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
    public void AssignToCustomer_TransfersOwnership()
    {
        var customerId = Guid.NewGuid();
        var basket = Basket.CreateAnonymous(Guid.NewGuid(), "tenant-1");

        basket.AssignToCustomer(customerId);

        Assert.Equal(customerId, basket.CustomerId);
        Assert.Null(basket.AnonymousToken);
    }
}

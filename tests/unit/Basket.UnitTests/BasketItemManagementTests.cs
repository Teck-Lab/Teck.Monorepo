using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketItemManagementTests
{
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void CreateForCustomer_StartsActiveAndEmpty()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");

        Assert.Equal(BasketStatus.Active, basket.Status);
        Assert.Empty(basket.Items);
        Assert.Equal(0m, basket.Subtotal);
        Assert.Null(basket.AnonymousToken);
    }

    [Fact]
    public void AddItem_SameProductTwice_MergesAndSumsQuantity()
    {
        var basket = Basket.CreateAnonymous(Guid.NewGuid(), "tenant-1");

        basket.AddItem(Product, "Widget", 10m, 2);
        basket.AddItem(Product, "Widget", 10m, 3);

        BasketItem line = Assert.Single(basket.Items);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(50m, basket.Subtotal);
    }

    [Fact]
    public void UpdateItemQuantity_ToZero_RemovesLine()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Product, "Widget", 10m, 2);

        basket.UpdateItemQuantity(Product, 0);

        Assert.Empty(basket.Items);
    }

    [Fact]
    public void RemoveItem_RemovesTheMatchingLine()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Product, "Widget", 10m, 2);

        basket.RemoveItem(Product);

        Assert.Empty(basket.Items);
    }

    [Fact]
    public void AddItem_WithNonPositiveQuantity_Throws()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");

        Assert.Throws<ArgumentOutOfRangeException>(() => basket.AddItem(Product, "Widget", 10m, 0));
    }
}

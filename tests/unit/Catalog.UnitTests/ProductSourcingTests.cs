using System.Linq;
using Catalog.Domain.DomainEvents;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests;

public sealed class ProductSourcingTests
{
    private static Product NewProduct() =>
        Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));

    private static Guid DefaultVariantId(Product p) => p.Variants[0].Id;

    [Fact]
    public void ChangeVariantSellPrice_WithNewAmount_UpdatesAndRaisesEvent()
    {
        var product = NewProduct();
        var variantId = DefaultVariantId(product);

        product.ChangeVariantSellPrice(variantId, new Money(14.00m, "USD"));

        Assert.Equal(new Money(14.00m, "USD"), product.Variants[0].SellPrice);
        var evt = Assert.Single(product.DomainEvents.OfType<VariantSellPriceChanged>());
        Assert.Equal(9.99m, evt.OldAmount);
        Assert.Equal(14.00m, evt.NewAmount);
        Assert.Equal("USD", evt.Currency);
    }

    [Fact]
    public void ChangeVariantSellPrice_WithSameAmount_DoesNotRaiseEvent()
    {
        var product = NewProduct();
        var variantId = DefaultVariantId(product);

        product.ChangeVariantSellPrice(variantId, new Money(9.99m, "USD"));

        Assert.Empty(product.DomainEvents.OfType<VariantSellPriceChanged>());
    }

    [Fact]
    public void LinkSupplier_AddsLinkWithDetailsAndInitialHistory()
    {
        var product = NewProduct();
        var variantId = DefaultVariantId(product);
        var supplierId = Guid.NewGuid();

        product.LinkSupplier(variantId, supplierId, new Money(5m, "USD"), "ACME-9", leadTimeDays: 7, minOrderQuantity: 10, isPreferred: true);

        var link = Assert.Single(product.Variants[0].Suppliers);
        Assert.Equal(supplierId, link.SupplierId);
        Assert.Equal(new Money(5m, "USD"), link.CostPrice);
        Assert.Equal("ACME-9", link.SupplierSku);
        Assert.Equal(7, link.LeadTimeDays);
        Assert.Equal(10, link.MinOrderQuantity);
        Assert.True(link.IsPreferred);
        Assert.Single(link.PriceHistory);
    }

    [Fact]
    public void LinkSupplier_SecondPreferred_ClearsFirstPreferred()
    {
        var product = NewProduct();
        var variantId = DefaultVariantId(product);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        product.LinkSupplier(variantId, first, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);

        product.LinkSupplier(variantId, second, new Money(6m, "USD"), "B", 7, 1, isPreferred: true);

        var suppliers = product.Variants[0].Suppliers;
        Assert.Equal(1, suppliers.Count(s => s.IsPreferred));
        Assert.True(suppliers.Single(s => s.SupplierId == second).IsPreferred);
    }

    [Fact]
    public void ChangeSupplierCost_AppendsHistoryAndRaisesEvent()
    {
        var product = NewProduct();
        var variantId = DefaultVariantId(product);
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(variantId, supplierId, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);

        product.ChangeSupplierCost(variantId, supplierId, new Money(6.50m, "USD"));

        var link = product.Variants[0].Suppliers.Single(s => s.SupplierId == supplierId);
        Assert.Equal(new Money(6.50m, "USD"), link.CostPrice);
        Assert.Equal(2, link.PriceHistory.Count);
        var evt = Assert.Single(product.DomainEvents.OfType<SupplierCostPriceChanged>());
        Assert.Equal(5m, evt.OldAmount);
        Assert.Equal(6.50m, evt.NewAmount);
    }

    [Fact]
    public void SetPreferredSupplier_MakesExactlyOnePreferred()
    {
        var product = NewProduct();
        var variantId = DefaultVariantId(product);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        product.LinkSupplier(variantId, a, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        product.LinkSupplier(variantId, b, new Money(6m, "USD"), "B", 7, 1, isPreferred: false);

        product.SetPreferredSupplier(variantId, b);

        var suppliers = product.Variants[0].Suppliers;
        Assert.Equal(1, suppliers.Count(s => s.IsPreferred));
        Assert.True(suppliers.Single(s => s.SupplierId == b).IsPreferred);
    }

    [Fact]
    public void Deactivate_CascadesToVariants()
    {
        var product = NewProduct();
        product.AddVariant("WIDGET-2", new Money(12m, "USD"), []);

        product.Deactivate();

        Assert.False(product.IsActive);
        Assert.All(product.Variants, v => Assert.False(v.IsActive));
    }

    [Fact]
    public void ChangeSupplierCost_WithSameCost_DoesNotRaiseEvent()
    {
        var product = NewProduct();
        var variantId = DefaultVariantId(product);
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(variantId, supplierId, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);

        product.ChangeSupplierCost(variantId, supplierId, new Money(5m, "USD"));

        Assert.Empty(product.DomainEvents.OfType<SupplierCostPriceChanged>());
        Assert.Single(product.Variants[0].Suppliers.Single(s => s.SupplierId == supplierId).PriceHistory);
    }
}

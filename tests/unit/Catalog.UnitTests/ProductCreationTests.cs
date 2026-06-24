using System.Linq;
using Catalog.Domain.DomainEvents;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests;

public sealed class ProductCreationTests
{
    private static Product NewProduct() =>
        Product.Create("tenant-1", "Widget", "A widget", null, "WIDGET-1", new Money(9.99m, "USD"));

    [Fact]
    public void Create_SetsPropertiesAndIsActive()
    {
        var product = NewProduct();

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("tenant-1", product.TenantId);
        Assert.Equal("Widget", product.Name);
        Assert.Equal("A widget", product.Description);
        Assert.Null(product.CategoryId);
        Assert.True(product.IsActive);
    }

    [Fact]
    public void Create_AddsSingleDefaultVariant()
    {
        var product = NewProduct();

        var variant = Assert.Single(product.Variants);
        Assert.True(variant.IsDefault);
        Assert.Equal("WIDGET-1", variant.Sku);
        Assert.Equal(new Money(9.99m, "USD"), variant.SellPrice);
        Assert.True(variant.IsActive);
    }

    [Fact]
    public void Create_RaisesProductCreatedWithVariantId()
    {
        var product = NewProduct();

        var evt = Assert.Single(product.DomainEvents.OfType<ProductCreated>());
        Assert.Equal(product.Id, evt.ProductId);
        Assert.Equal("tenant-1", evt.TenantId);
        Assert.Equal(product.Variants[0].Id, Assert.Single(evt.VariantIds));
    }

    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Product.Create("tenant-1", " ", null, null, "SKU", new Money(1m, "USD")));
    }

    [Fact]
    public void AddVariant_AppendsVariantAndRaisesVariantCreated()
    {
        var product = NewProduct();

        var variantId = product.AddVariant(
            "WIDGET-2",
            new Money(12.50m, "USD"),
            [new VariantAttribute("Size", "Large")]);

        Assert.Equal(2, product.Variants.Count);
        var added = product.Variants.Single(v => v.Id == variantId);
        Assert.False(added.IsDefault);
        Assert.Equal("WIDGET-2", added.Sku);
        Assert.Equal("Large", Assert.Single(added.Attributes).Value);
        var evt = Assert.Single(product.DomainEvents.OfType<VariantCreated>());
        Assert.Equal(variantId, evt.VariantId);
    }
}

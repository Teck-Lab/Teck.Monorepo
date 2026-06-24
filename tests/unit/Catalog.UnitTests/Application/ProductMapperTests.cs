using Catalog.Application.Products.Mapping;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class ProductMapperTests
{
    [Fact]
    public void ToDto_FlattensVariantSellPriceAndAttributes()
    {
        var product = Product.Create("tenant-1", "Widget", "desc", null, "WIDGET-1", new Money(9.99m, "USD"));
        product.AddVariant("WIDGET-2", new Money(12.50m, "USD"), [new VariantAttribute("Size", "Large")]);

        var dto = product.ToDto();

        Assert.Equal(product.Id, dto.Id);
        Assert.Equal("Widget", dto.Name);
        Assert.Equal(2, dto.Variants.Count);
        var defaultVariant = dto.Variants.Single(v => v.IsDefault);
        Assert.Equal("WIDGET-1", defaultVariant.Sku);
        Assert.Equal(9.99m, defaultVariant.SellPriceAmount);
        Assert.Equal("USD", defaultVariant.SellPriceCurrency);
        var added = dto.Variants.Single(v => !v.IsDefault);
        Assert.Equal("Large", Assert.Single(added.Attributes).Value);
    }

    [Fact]
    public void ToDto_MapsCategory()
    {
        var category = Category.Create("tenant-1", "Beverages", "beverages");

        var dto = category.ToDto();

        Assert.Equal(category.Id, dto.Id);
        Assert.Equal("Beverages", dto.Name);
        Assert.Equal("beverages", dto.Slug);
        Assert.Null(dto.ParentId);
    }

    [Fact]
    public void ToSummaries_MapsEachProduct()
    {
        var a = Product.Create("tenant-1", "A", null, null, "A-1", new Money(1m, "USD"));
        var b = Product.Create("tenant-1", "B", null, null, "B-1", new Money(2m, "USD"));

        var summaries = new[] { a, b }.ToSummaries();

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.Name == "A");
        Assert.Contains(summaries, s => s.Name == "B");
    }
}

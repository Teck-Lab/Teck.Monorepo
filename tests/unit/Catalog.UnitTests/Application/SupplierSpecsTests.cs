using Ardalis.Specification;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class SupplierSpecsTests
{
    [Fact]
    public void SupplierByIdSpec_MatchesOnlyTheTarget()
    {
        var target = Supplier.Create("tenant-1", "Acme");
        var other = Supplier.Create("tenant-1", "Other");

        var result = new SupplierByIdSpec(target.Id).Evaluate(new[] { target, other }).ToList();

        Assert.Equal(target.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void ProductByVariantSpec_FindsOwningProduct()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var variantId = product.Variants[0].Id;
        var other = Product.Create("tenant-1", "Other", null, null, "OTHER-1", new Money(1m, "USD"));

        var result = new ProductByVariantSpec(variantId).Evaluate(new[] { product, other }).ToList();

        Assert.Equal(product.Id, Assert.Single(result).Id);
    }
}

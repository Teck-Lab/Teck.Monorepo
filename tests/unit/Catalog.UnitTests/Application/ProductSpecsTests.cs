using Ardalis.Specification;
using Catalog.Application.Products.ReadModels;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class ProductSpecsTests
{
    private static Product Make(string name, Guid? categoryId) =>
        Product.Create("tenant-1", name, null, categoryId, $"{name}-1", new Money(1m, "USD"));

    [Fact]
    public void ProductByIdSpec_MatchesOnlyTheTargetProduct()
    {
        var target = Make("A", null);
        var other = Make("B", null);

        var result = new ProductByIdSpec(target.Id).Evaluate(new[] { target, other }).ToList();

        Assert.Equal(target.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void ProductsByCategorySpec_WithCategory_FiltersByCategory()
    {
        var categoryId = Guid.NewGuid();
        var inCategory = Make("A", categoryId);
        var outOfCategory = Make("B", Guid.NewGuid());

        var result = new ProductsByCategorySpec(categoryId).Evaluate(new[] { inCategory, outOfCategory }).ToList();

        Assert.Equal(inCategory.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void ProductsByCategorySpec_WithoutCategory_ReturnsAllOrderedByName()
    {
        var b = Make("B", null);
        var a = Make("A", null);

        var result = new ProductsByCategorySpec(null).Evaluate(new[] { b, a }).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].Name);
    }
}

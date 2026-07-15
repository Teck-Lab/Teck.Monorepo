using Ardalis.Specification;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Domain.Entities;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>Tests for the Phase-1 stock read-model specifications.</summary>
public sealed class StockSpecsTests
{
    private static StockItem Make(Guid productId, Guid locationId) =>
        StockItem.Create(productId, locationId, "tenant-1", 10, allowBackorder: false, reorderThreshold: 2);

    /// <summary>The spec must match only the stock item for the given product at the given location.</summary>
    [Fact]
    public void StockItemByProductLocationSpec_MatchesOnlyTheTargetProductAndLocation()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var target = Make(productId, locationId);
        var sameProductOtherLocation = Make(productId, Guid.NewGuid());
        var sameLocationOtherProduct = Make(Guid.NewGuid(), locationId);

        var result = new StockItemByProductLocationSpec("tenant-1", productId, locationId)
            .Evaluate(new[] { target, sameProductOtherLocation, sameLocationOtherProduct })
            .ToList();

        Assert.Equal(target.Id, Assert.Single(result).Id);
    }

    /// <summary>The spec must match every stock record for the product across all locations.</summary>
    [Fact]
    public void StockItemsByProductSpec_MatchesAllLocationsForTheProduct()
    {
        var productId = Guid.NewGuid();
        var locationA = Make(productId, Guid.NewGuid());
        var locationB = Make(productId, Guid.NewGuid());
        var otherProduct = Make(Guid.NewGuid(), Guid.NewGuid());

        var result = new StockItemsByProductSpec(productId)
            .Evaluate(new[] { locationA, locationB, otherProduct })
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, item => item.Id == otherProduct.Id);
    }

    /// <summary>The spec must match every stock record for the product across all locations, for summing availability.</summary>
    [Fact]
    public void AvailabilityByProductSpec_MatchesAllLocationsForTheProduct()
    {
        var productId = Guid.NewGuid();
        var locationA = Make(productId, Guid.NewGuid());
        var locationB = Make(productId, Guid.NewGuid());
        var otherProduct = Make(Guid.NewGuid(), Guid.NewGuid());

        var result = new AvailabilityByProductSpec(productId)
            .Evaluate(new[] { locationA, locationB, otherProduct })
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, item => item.Id == otherProduct.Id);
    }
}

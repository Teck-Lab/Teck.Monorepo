using System.Linq;
using Inventories.Domain.Entities;
using Inventories.Domain.Services;
using Inventories.Domain.ValueObjects;
using Xunit;

namespace Inventories.UnitTests;

public sealed class StockAllocatorTests
{
    [Fact]
    public void Allocate_WhenAvailableCoversRequest_FillsInPriorityOrder()
    {
        Guid locationA = Guid.NewGuid();
        Guid locationB = Guid.NewGuid();
        StockItem[] items =
        [
            CreateStockItem(locationA, quantityOnHand: 5, allowBackorder: false),
            CreateStockItem(locationB, quantityOnHand: 4, allowBackorder: false),
        ];

        AllocationResult result = StockAllocator.Allocate(items, requestedQuantity: 7);

        Assert.True(result.Satisfied);
        Assert.Equal(0, result.BackorderedQuantity);
        Assert.Equal(
            [new Allocation(locationA, 5), new Allocation(locationB, 2)],
            result.Allocations);
    }

    [Fact]
    public void Allocate_WhenShortfallAndNoBackorderAllowed_IsNotSatisfied()
    {
        StockItem[] items =
        [
            CreateStockItem(Guid.NewGuid(), quantityOnHand: 3, allowBackorder: false),
            CreateStockItem(Guid.NewGuid(), quantityOnHand: 2, allowBackorder: false),
        ];

        AllocationResult result = StockAllocator.Allocate(items, requestedQuantity: 10);

        Assert.False(result.Satisfied);
    }

    [Fact]
    public void Allocate_WhenShortfallAndTailItemAllowsBackorder_AbsorbsRemainderAsBackordered()
    {
        StockItem[] items =
        [
            CreateStockItem(Guid.NewGuid(), quantityOnHand: 3, allowBackorder: false),
            CreateStockItem(Guid.NewGuid(), quantityOnHand: 2, allowBackorder: true),
        ];

        AllocationResult result = StockAllocator.Allocate(items, requestedQuantity: 10);

        Assert.True(result.Satisfied);
        Assert.Equal(5, result.Allocations.Sum(allocation => allocation.Quantity));
        Assert.Equal(5, result.BackorderedQuantity);
    }

    private static StockItem CreateStockItem(Guid locationId, int quantityOnHand, bool allowBackorder) =>
        StockItem.Create(
            productId: Guid.NewGuid(),
            locationId: locationId,
            tenantId: "tenant-1",
            quantityOnHand: quantityOnHand,
            allowBackorder: allowBackorder,
            reorderThreshold: 0);
}

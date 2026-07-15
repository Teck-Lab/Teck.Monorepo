using Inventories.Domain.Entities;
using Xunit;

namespace Inventories.UnitTests;

public sealed class StockItemTests
{
    private static StockItem New(int onHand, bool backorder = false, int reorder = 0) =>
        StockItem.Create(Guid.NewGuid(), Guid.NewGuid(), "t1", onHand, backorder, reorder);

    [Fact]
    public void Reserve_BeyondAvailable_WithoutBackorder_Throws()
    {
        var item = New(5);
        Assert.Throws<InvalidOperationException>(() => item.Reserve(6));
    }

    [Fact]
    public void Reserve_BeyondAvailable_WithBackorder_GoesNegative()
    {
        var item = New(5, backorder: true);
        item.Reserve(8);
        Assert.Equal(-3, item.Available);
        Assert.Equal(8, item.QuantityReserved);
    }

    [Fact]
    public void Release_ClampsAtZero()
    {
        var item = New(5);
        item.Reserve(2);
        item.Release(5);
        Assert.Equal(0, item.QuantityReserved);
    }

    [Fact]
    public void IsDepleted_WhenAvailableReachesZero()
    {
        var item = New(2);
        item.Reserve(2);
        Assert.True(item.IsDepleted());
    }
}

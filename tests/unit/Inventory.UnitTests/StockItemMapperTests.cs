using Inventories.Application.Inventory.Mapping;
using Inventories.Domain.Entities;
using Xunit;

namespace Inventories.UnitTests;

public sealed class StockItemMapperTests
{
    [Fact]
    public void ToDto_MapsAvailableQuantity()
    {
        var item = StockItem.Create(Guid.NewGuid(), Guid.NewGuid(), "t1", 10, allowBackorder: false, reorderThreshold: 2);
        item.Reserve(3);

        var dto = StockItemMapper.ToDto(item);

        Assert.Equal(item.Available, dto.Available);
    }
}

using Baskets.Application.Baskets.Mapping;
using Baskets.Domain.Entities;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketMapperTests
{
    [Fact]
    public void ToDto_MapsStatusNameAndItems()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 10m, 2);

        var dto = BasketMapper.ToDto(basket);

        Assert.Equal("Active", dto.Status);
        Assert.Equal(20m, dto.Subtotal);
        Assert.Single(dto.Items);
        Assert.Equal(20m, dto.Items[0].LineTotal);
    }
}

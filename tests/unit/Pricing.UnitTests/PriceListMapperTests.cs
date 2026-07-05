using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceListMapperTests
{
    [Fact]
    public void ToDto_FlattensScopeStatusAndPrices()
    {
        var list = PriceList.Create("Retail", new PriceScope("USD", "US", null, null), null, null, "tenant-1");
        list.AddOrUpdatePrice(Guid.NewGuid(), new Money(10m, "USD"), [new PriceTier(1, new Money(10m, "USD"))]);
        list.Activate();

        PriceListDto dto = list.ToDto();

        Assert.Equal("Active", dto.Status);
        Assert.Equal("USD", dto.Currency);
        Assert.Equal("US", dto.Country);
        PriceDto price = Assert.Single(dto.Prices);
        Assert.Equal(10m, price.Amount);
        Assert.Equal("USD", price.Currency);
        Assert.Single(price.Tiers);
    }
}

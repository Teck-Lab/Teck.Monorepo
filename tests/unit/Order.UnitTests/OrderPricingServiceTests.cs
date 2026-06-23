using Order.Domain.Entities;
using Order.Domain.Services;
using Xunit;

namespace Order.UnitTests;

public sealed class OrderPricingServiceTests
{
    [Fact]
    public void CalculateTotal_WithValidLines_ReturnsCorrectSum()
    {
        List<OrderLine> lines =
        [
            new OrderLine(Guid.NewGuid(), "Product A", 2, 10m),
            new OrderLine(Guid.NewGuid(), "Product B", 1, 5.25m)
        ];

        var total = OrderPricingService.CalculateTotal(lines);

        Assert.Equal(25.25m, total);
    }

    [Fact]
    public void CalculateTotal_WithEmptyLines_ReturnsZero()
    {
        List<OrderLine> lines = [];

        var total = OrderPricingService.CalculateTotal(lines);

        Assert.Equal(0m, total);
    }

    [Fact]
    public void CalculateTotal_WithNegativeQuantity_Throws()
    {
        List<OrderLine> lines = [new OrderLine(Guid.NewGuid(), "Product", -1, 10m)];

        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricingService.CalculateTotal(lines));
    }
}

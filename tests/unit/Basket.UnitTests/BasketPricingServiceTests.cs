using Baskets.Domain.Services;
using Baskets.Domain.ValueObjects;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketPricingServiceTests
{
    [Fact]
    public void CalculateSubtotal_WithMultipleItems_ReturnsSumOfLineTotals()
    {
        BasketItem[] items =
        [
            new(Guid.NewGuid(), "A", 10m, 2),
            new(Guid.NewGuid(), "B", 5m, 3),
        ];

        decimal subtotal = BasketPricingService.CalculateSubtotal(items);

        Assert.Equal(35m, subtotal);
    }

    [Fact]
    public void CalculateSubtotal_WithNegativeQuantity_Throws()
    {
        BasketItem[] items = [new(Guid.NewGuid(), "A", 10m, -1)];

        Assert.Throws<ArgumentOutOfRangeException>(() => BasketPricingService.CalculateSubtotal(items));
    }
}

using Pricing.Domain.Services;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class CurrencyConverterTests
{
    [Fact]
    public void Convert_MultipliesByRate_AndSetsTargetCurrency()
    {
        var result = CurrencyConverter.Convert(new Money(10m, "USD"), "EUR", 0.9m, 2, MidpointRounding.ToEven);

        Assert.Equal(9.00m, result.Amount);
        Assert.Equal("EUR", result.Currency);
    }

    [Fact]
    public void Convert_RoundsToBankersRounding()
    {
        // 10.125 * 1 = 10.125 -> ToEven at 2dp -> 10.12
        var result = CurrencyConverter.Convert(new Money(10.125m, "USD"), "USD", 1m, 2, MidpointRounding.ToEven);

        Assert.Equal(10.12m, result.Amount);
    }

    [Fact]
    public void Convert_NonPositiveRate_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CurrencyConverter.Convert(new Money(10m, "USD"), "EUR", 0m, 2, MidpointRounding.ToEven));
}

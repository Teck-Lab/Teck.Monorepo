using Pricing.Domain.Entities;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ExchangeRateTests
{
    [Fact]
    public void Create_NonPositiveRate_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExchangeRate.Create("USD", "EUR", 0m, null, null, "tenant-1"));

    [Fact]
    public void Create_SameCurrency_Throws() =>
        Assert.Throws<ArgumentException>(
            () => ExchangeRate.Create("USD", "usd", 1m, null, null, "tenant-1"));

    [Fact]
    public void IsValidAt_OpenWindow_AlwaysValid()
    {
        var rate = ExchangeRate.Create("USD", "EUR", 0.9m, null, null, "tenant-1");

        Assert.True(rate.IsValidAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsValidAt_OutsideWindow_False()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var until = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var rate = ExchangeRate.Create("USD", "EUR", 0.9m, from, until, "tenant-1");

        Assert.False(rate.IsValidAt(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(rate.IsValidAt(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)));
    }
}

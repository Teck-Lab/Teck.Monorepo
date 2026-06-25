using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests;

public sealed class MoneyTests
{
    [Fact]
    public void Constructor_WithValidValues_SetsProperties()
    {
        var money = new Money(12.50m, "USD");

        Assert.Equal(12.50m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void Equals_WithSameAmountAndCurrency_AreEqual()
    {
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_WithDifferentCurrency_AreNotEqual()
    {
        var a = new Money(10m, "USD");
        var b = new Money(10m, "EUR");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Constructor_WithNegativeAmount_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Money(-1m, "USD"));
    }

    [Fact]
    public void Constructor_WithBlankCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Money(1m, " "));
    }
}

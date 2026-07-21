using Billings.Domain.ValueObjects;
using Xunit;

namespace Billing.UnitTests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void Ctor_SetsAmountAndCurrency()
    {
        var money = new Money(10.50m, "USD");

        Assert.Equal(10.50m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void Ctor_NegativeAmount_Throws() =>
        Assert.Throws<ArgumentException>(() => new Money(-1m, "USD"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_BlankCurrency_Throws(string currency) =>
        Assert.Throws<ArgumentException>(() => new Money(1m, currency));

    [Fact]
    public void Equals_SameAmountAndCurrency_AreEqual()
    {
        var left = new Money(5m, "EUR");
        var right = new Money(5m, "EUR");

        Assert.Equal(left, right);
    }

    [Fact]
    public void Equals_DifferentCurrency_AreNotEqual()
    {
        var left = new Money(5m, "EUR");
        var right = new Money(5m, "USD");

        Assert.NotEqual(left, right);
    }
}

using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ValueObjectTests
{
    [Fact]
    public void Money_NegativeAmount_Throws() =>
        Assert.Throws<ArgumentException>(() => new Money(-1m, "USD"));

    [Fact]
    public void Money_BlankCurrency_Throws() =>
        Assert.Throws<ArgumentException>(() => new Money(1m, " "));

    [Fact]
    public void PriceScope_NullDimensions_AreWildcardsAndCompatibleWithAnything()
    {
        var scope = new PriceScope("USD", country: null, customerGroupId: null, channelId: null);

        Assert.True(scope.IsCompatibleWith("US", Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(0, scope.Specificity);
    }

    [Fact]
    public void PriceScope_SetDimension_RequiresExactMatch()
    {
        var group = Guid.NewGuid();
        var scope = new PriceScope("USD", country: "US", customerGroupId: group, channelId: null);

        Assert.True(scope.IsCompatibleWith("US", group, Guid.NewGuid()));
        Assert.False(scope.IsCompatibleWith("DE", group, null));    // country mismatch
        Assert.False(scope.IsCompatibleWith("US", Guid.NewGuid(), null)); // group mismatch
        Assert.False(scope.IsCompatibleWith(null, group, null));    // request lacks a set dimension
        Assert.Equal(2, scope.Specificity);
    }

    [Fact]
    public void PriceListStatus_FromValue_RoundTrips() =>
        Assert.Equal(PriceListStatus.Active, PriceListStatus.FromValue(2));
}

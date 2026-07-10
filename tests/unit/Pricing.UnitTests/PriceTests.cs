using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceTests
{
    private static Price NewPrice(params PriceTier[] tiers) =>
        Price.Create(Guid.NewGuid(), new Money(10m, "USD"), tiers, "tenant-1");

    [Fact]
    public void UnitAmountFor_NoTiers_ReturnsBaseAmount()
    {
        var price = NewPrice();

        Assert.Equal(10m, price.UnitAmountFor(5).Amount);
    }

    [Fact]
    public void UnitAmountFor_PicksHighestApplicableTier()
    {
        var price = NewPrice(new PriceTier(1, new Money(10m, "USD")), new PriceTier(10, new Money(8m, "USD")));

        Assert.Equal(10m, price.UnitAmountFor(9).Amount);
        Assert.Equal(8m, price.UnitAmountFor(10).Amount);
        Assert.Equal(8m, price.UnitAmountFor(100).Amount);
    }

    [Fact]
    public void Create_TierWithForeignCurrency_Throws() =>
        Assert.Throws<ArgumentException>(() => NewPrice(new PriceTier(1, new Money(10m, "EUR"))));

    [Fact]
    public void Create_NonAscendingTiers_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            NewPrice(new PriceTier(10, new Money(8m, "USD")), new PriceTier(5, new Money(9m, "USD"))));

    [Fact]
    public void Create_TierBelowOne_Throws() =>
        Assert.Throws<ArgumentException>(() => NewPrice(new PriceTier(0, new Money(10m, "USD"))));
}

using Pricing.Domain.Entities;
using Pricing.Domain.Services;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceResolutionServiceTests
{
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static Price PriceIn(PriceList list)
    {
        // AddOrUpdatePrice creates the Price inside the list; return it with its navigation set.
        Price price = Assert.Single(list.Prices);
        typeof(Price).GetProperty(nameof(Price.PriceList))!.SetValue(price, list);
        return price;
    }

    private static (PriceList List, Price Price) ActiveList(PriceScope scope, decimal amount, params PriceTier[] tiers)
    {
        var list = PriceList.Create("l", scope, null, null, "tenant-1");
        list.AddOrUpdatePrice(Product, new Money(amount, scope.Currency), tiers);
        list.Activate();
        return (list, PriceIn(list));
    }

    private static PriceResolutionContext Ctx(string currency = "USD", int qty = 1, string? country = null, Guid? group = null, Guid? channel = null) =>
        new(currency, qty, country, group, channel, Now);

    [Fact]
    public void SelectBest_NoCandidates_ReturnsNull() =>
        Assert.Null(PriceResolutionService.SelectBest([], Ctx()));

    [Fact]
    public void SelectBest_DraftList_Ignored()
    {
        var list = PriceList.Create("l", new PriceScope("USD", null, null, null), null, null, "tenant-1");
        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);
        // not activated -> Draft
        Price price = PriceIn(list);

        Assert.Null(PriceResolutionService.SelectBest([price], Ctx()));
    }

    [Fact]
    public void SelectBest_MostSpecificScopeWins()
    {
        var group = Guid.NewGuid();
        var (_, general) = ActiveList(new PriceScope("USD", null, null, null), 10m);
        var (_, specific) = ActiveList(new PriceScope("USD", null, group, null), 8m);

        ResolvedSelection? result = PriceResolutionService.SelectBest([general, specific], Ctx(group: group));

        Assert.NotNull(result);
        Assert.Equal(8m, result!.UnitAmount.Amount);
    }

    [Fact]
    public void SelectBest_PrefersNativeCurrencyOverMoreSpecificForeign()
    {
        var group = Guid.NewGuid();
        var (_, nativeGeneral) = ActiveList(new PriceScope("USD", null, null, null), 10m);
        var (_, foreignSpecific) = ActiveList(new PriceScope("EUR", null, group, null), 5m);

        ResolvedSelection? result = PriceResolutionService.SelectBest([nativeGeneral, foreignSpecific], Ctx(currency: "USD", group: group));

        Assert.NotNull(result);
        Assert.Equal("USD", result!.UnitAmount.Currency);
        Assert.Equal(10m, result.UnitAmount.Amount);
    }

    [Fact]
    public void SelectBest_AppliesQuantityTier()
    {
        var (_, tiered) = ActiveList(new PriceScope("USD", null, null, null), 10m, new PriceTier(1, new Money(10m, "USD")), new PriceTier(10, new Money(8m, "USD")));

        ResolvedSelection? result = PriceResolutionService.SelectBest([tiered], Ctx(qty: 10));

        Assert.Equal(8m, result!.UnitAmount.Amount);
    }

    [Fact]
    public void SelectBest_IncompatibleScope_Excluded()
    {
        var (_, deOnly) = ActiveList(new PriceScope("USD", "DE", null, null), 10m);

        Assert.Null(PriceResolutionService.SelectBest([deOnly], Ctx(country: "US")));
    }
}

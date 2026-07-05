using Pricing.Domain.Entities;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceListTests
{
    private static readonly Guid Product = Guid.NewGuid();

    private static PriceList Draft() =>
        PriceList.Create("Default", new PriceScope("USD", null, null, null), validFrom: null, validUntil: null, "tenant-1");

    [Fact]
    public void Create_StartsDraft_WithNoPrices()
    {
        var list = Draft();

        Assert.Equal(PriceListStatus.Draft, list.Status);
        Assert.Empty(list.Prices);
    }

    [Fact]
    public void AddOrUpdatePrice_OnDraft_DoesNotRaisePriceChanged()
    {
        var list = Draft();

        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);

        Assert.Single(list.Prices);
        Assert.Empty(list.DomainEvents.OfType<PriceChanged>());
    }

    [Fact]
    public void AddOrUpdatePrice_ForeignCurrency_Throws()
    {
        var list = Draft();

        Assert.Throws<ArgumentException>(() => list.AddOrUpdatePrice(Product, new Money(10m, "EUR"), []));
    }

    [Fact]
    public void AddOrUpdatePrice_Twice_UpdatesInPlace()
    {
        var list = Draft();
        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);
        list.AddOrUpdatePrice(Product, new Money(12m, "USD"), []);

        Price price = Assert.Single(list.Prices);
        Assert.Equal(12m, price.Amount.Amount);
    }

    [Fact]
    public void Activate_RaisesUpsertedPerPrice()
    {
        var list = Draft();
        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);

        list.Activate();

        Assert.Equal(PriceListStatus.Active, list.Status);
        PriceChanged evt = Assert.Single(list.DomainEvents.OfType<PriceChanged>());
        Assert.Equal(PriceChangeType.Upserted, evt.ChangeType);
        Assert.Equal(Product, evt.ProductId);
    }

    [Fact]
    public void AddOrUpdatePrice_OnActive_RaisesUpserted()
    {
        var list = Draft();
        list.Activate();

        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);

        Assert.Contains(list.DomainEvents.OfType<PriceChanged>(), e => e.ChangeType == PriceChangeType.Upserted);
    }

    [Fact]
    public void Archive_RaisesRemovedPerPrice()
    {
        var list = Draft();
        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);
        list.Activate();

        list.Archive();

        Assert.Equal(PriceListStatus.Archived, list.Status);
        Assert.Contains(list.DomainEvents.OfType<PriceChanged>(), e => e.ChangeType == PriceChangeType.Removed);
    }

    [Fact]
    public void RemovePrice_Missing_Throws()
    {
        var list = Draft();

        Assert.Throws<InvalidOperationException>(() => list.RemovePrice(Product));
    }

    [Fact]
    public void Create_InvalidValidityWindow_Throws()
    {
        var from = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            PriceList.Create("x", new PriceScope("USD", null, null, null), validFrom: from, validUntil: from.AddDays(-1), "tenant-1"));
    }
}

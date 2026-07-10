using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pricing.Application.Database;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Infrastructure.MultiTenant;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PricingDbContextTests
{
    [Fact]
    public void Model_BuildsWithoutError()
    {
        using var context = CreateInMemory("pricing-model-test");

        // Forcing model creation validates every IEntityTypeConfiguration (owned Money, tiers, scope).
        Assert.NotNull(context.Model.FindEntityType(typeof(PriceList)));
        Assert.NotNull(context.Model.FindEntityType(typeof(Price)));
        Assert.NotNull(context.Model.FindEntityType(typeof(ExchangeRate)));
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsPriceListWithTieredPrices()
    {
        var name = $"pricing-roundtrip-{Guid.NewGuid()}";
        var scope = new PriceScope("USD", country: null, customerGroupId: null, channelId: null);
        PriceList list = PriceList.Create("Standard", scope, validFrom: null, validUntil: null, tenantId: "tenant-1");
        Guid productId = Guid.NewGuid();
        list.AddOrUpdatePrice(
            productId,
            new Money(10m, "USD"),
            [new PriceTier(10, new Money(8m, "USD")), new PriceTier(100, new Money(5.5m, "USD"))]);

        using (PricingDbContext db = CreateInMemory(name))
        {
            db.PriceLists.Add(list);
            await db.SaveChangesAsync();
        }

        using (PricingDbContext db = CreateInMemory(name))
        {
            PriceList? reloaded = await db.PriceLists
                .Include(l => l.Prices)
                .FirstOrDefaultAsync();

            Assert.NotNull(reloaded);
            Price price = Assert.Single(reloaded!.Prices);
            Assert.Equal(productId, price.ProductId);
            Assert.Equal(10m, price.Amount.Amount);
            Assert.Equal("USD", price.Amount.Currency);

            Assert.Equal(2, price.Tiers.Count);
            Assert.Equal(10, price.Tiers[0].MinQuantity);
            Assert.Equal(8m, price.Tiers[0].Amount.Amount);
            Assert.Equal("USD", price.Tiers[0].Amount.Currency);
            Assert.Equal(100, price.Tiers[1].MinQuantity);
            Assert.Equal(5.5m, price.Tiers[1].Amount.Amount);
            Assert.Equal("USD", price.Tiers[1].Amount.Currency);
        }
    }

    private static PricingDbContext CreateInMemory(string name)
    {
        var options = new DbContextOptionsBuilder<PricingDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new PricingDbContext(options, Substitute.For<IMultiTenantContextAccessor<TenantDetails>>());
    }
}

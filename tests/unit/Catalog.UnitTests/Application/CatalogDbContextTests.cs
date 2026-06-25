using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CatalogDbContextTests
{
    [Fact]
    public void Model_BuildsWithoutError()
    {
        using var db = CatalogTestContext.CreateInMemory();

        // Accessing the model forces EF to build the owned-aggregate tree + Money mappings.
        Assert.NotNull(db.Model);
        Assert.NotNull(db.Model.FindEntityType(typeof(Product)));
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsProductAggregate()
    {
        var product = Product.Create("tenant-1", "Widget", "desc", null, "WIDGET-1", new Money(9.99m, "USD"));
        product.LinkSupplier(product.Variants[0].Id, Guid.NewGuid(), new Money(5m, "USD"), "ACME-1", 7, 10, isPreferred: true);

        using (var db = CatalogTestContext.CreateInMemory("roundtrip"))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        using (var db = CatalogTestContext.CreateInMemory("roundtrip"))
        {
            var reloaded = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Products);
            Assert.NotNull(reloaded);
            var variant = Assert.Single(reloaded!.Variants);
            Assert.Equal("WIDGET-1", variant.Sku);
            Assert.Equal(9.99m, variant.SellPrice.Amount);
            var link = Assert.Single(variant.Suppliers);
            Assert.Single(link.PriceHistory);
        }
    }
}

using Finbuckle.MultiTenant.Abstractions;
using Inventories.Application.Database;
using Inventories.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Infrastructure.MultiTenant;
using Xunit;

namespace Inventories.UnitTests;

public sealed class InventoryDbContextTests
{
    [Fact]
    public void Model_BuildsWithoutError()
    {
        using var db = CreateInMemory($"inventory-model-{Guid.NewGuid()}");

        Assert.NotNull(db.Model);
        Assert.NotNull(db.Model.FindEntityType(typeof(StockItem)));
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsStockItemAggregate()
    {
        var name = $"inventory-roundtrip-{Guid.NewGuid()}";
        var stockItem = StockItem.Create(
            productId: Guid.NewGuid(),
            locationId: Guid.NewGuid(),
            tenantId: "tenant-1",
            quantityOnHand: 100,
            allowBackorder: false,
            reorderThreshold: 10);

        stockItem.Reserve(15);

        using (var db = CreateInMemory(name))
        {
            db.StockItems.Add(stockItem);
            await db.SaveChangesAsync();
        }

        using (var db = CreateInMemory(name))
        {
            StockItem? reloaded = await db.StockItems.FirstOrDefaultAsync();

            Assert.NotNull(reloaded);
            Assert.Equal(stockItem.ProductId, reloaded!.ProductId);
            Assert.Equal(stockItem.LocationId, reloaded.LocationId);
            Assert.Equal("tenant-1", reloaded.TenantId);
            Assert.Equal(100, reloaded.QuantityOnHand);
            Assert.Equal(15, reloaded.QuantityReserved);
            Assert.False(reloaded.AllowBackorder);
            Assert.Equal(10, reloaded.ReorderThreshold);
        }
    }

    private static InventoryDbContext CreateInMemory(string name)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new InventoryDbContext(options, Substitute.For<IMultiTenantContextAccessor<TenantDetails>>());
    }
}

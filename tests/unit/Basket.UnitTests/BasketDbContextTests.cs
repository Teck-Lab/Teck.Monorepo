using Baskets.Application.Database;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Infrastructure.MultiTenant;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketDbContextTests
{
    [Fact]
    public void Model_BuildsWithoutError()
    {
        using var db = CreateInMemory($"basket-model-{Guid.NewGuid()}");

        // Accessing the model forces EF to build the owned BasketItem collection
        // and the BasketStatus SmartEnum conversion.
        Assert.NotNull(db.Model);
        Assert.NotNull(db.Model.FindEntityType(typeof(Basket)));
    }

    [Fact]
    public void Model_IncludesBasketWithOwnedItems()
    {
        using var db = CreateInMemory($"basket-model-nav-{Guid.NewGuid()}");

        var entity = db.Model.FindEntityType(typeof(Basket));
        Assert.NotNull(entity);
        Assert.NotNull(entity!.FindNavigation(nameof(Basket.Items)));
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsBasketAggregate()
    {
        var name = $"basket-roundtrip-{Guid.NewGuid()}";
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 12.50m, 2);

        using (var db = CreateInMemory(name))
        {
            db.Baskets.Add(basket);
            await db.SaveChangesAsync();
        }

        using (var db = CreateInMemory(name))
        {
            Basket? reloaded = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Baskets);

            Assert.NotNull(reloaded);
            BasketItem item = Assert.Single(reloaded!.Items);
            Assert.Equal("Widget", item.ProductName);
            Assert.Equal(25.00m, item.LineTotal);
            Assert.Equal(BasketStatus.Active, reloaded.Status);
        }
    }

    private static BasketDbContext CreateInMemory(string name)
    {
        var options = new DbContextOptionsBuilder<BasketDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new BasketDbContext(options, TenantAccessor());
    }

    private static IMultiTenantContextAccessor<TenantDetails> TenantAccessor()
    {
        var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        accessor.MultiTenantContext.Returns(new MultiTenantContext<TenantDetails>(new TenantDetails { Id = "tenant-1", Identifier = "tenant-1" }));
        return accessor;
    }
}

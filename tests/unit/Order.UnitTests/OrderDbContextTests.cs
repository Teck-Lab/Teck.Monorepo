using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Orders.Application.Database;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Infrastructure.MultiTenant;
using Xunit;

namespace Orders.UnitTests;

public sealed class OrderDbContextTests
{
    [Fact]
    public void Model_BuildsWithoutError()
    {
        using var db = CreateInMemory($"order-model-{Guid.NewGuid()}");

        // Accessing the model forces EF to build the owned OrderLine collection
        // and the OrderStatus SmartEnum conversion.
        Assert.NotNull(db.Model);
        Assert.NotNull(db.Model.FindEntityType(typeof(Order)));
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsOrderAggregate()
    {
        var name = $"order-roundtrip-{Guid.NewGuid()}";
        var order = Order.Create(
            Guid.NewGuid(),
            "tenant-1",
            [new OrderLine(Guid.NewGuid(), "Widget", 2, 12.50m)]);

        using (var db = CreateInMemory(name))
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        using (var db = CreateInMemory(name))
        {
            Order? reloaded = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Orders);

            Assert.NotNull(reloaded);
            OrderLine line = Assert.Single(reloaded!.Lines);
            Assert.Equal("Widget", line.ProductName);
            Assert.Equal(25.00m, line.Total);
            Assert.Equal(OrderStatus.Pending, reloaded.Status);
        }
    }

    private static OrderDbContext CreateInMemory(string name)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new OrderDbContext(options, Substitute.For<IMultiTenantContextAccessor<TenantDetails>>());
    }
}

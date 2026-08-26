using Microsoft.EntityFrameworkCore;
using Orders.Application.Database;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Orders.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class TenantIsolationTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task OrderContext_ForeignTenantOrder_IsExcluded()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(OrderDbContext), "Order.Host");
        Order foreignOrder = Order.Create(
            Guid.NewGuid(),
            "tenant-b",
            [new OrderLine(Guid.NewGuid(), "Foreign item", 1, 10m)]);

        await using (var seed = CreateContext(connectionString, "tenant-b"))
        {
            seed.Orders.Add(foreignOrder);
            await seed.SaveChangesAsync();
        }

        await using var tenantA = CreateContext(connectionString, "tenant-a");

        Assert.Null(await tenantA.Orders.SingleOrDefaultAsync(order => order.Id == foreignOrder.Id));
    }

    private static OrderDbContext CreateContext(string connectionString, string tenantId)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(tenantId)
            .Options;

        return new OrderDbContext(options, null!);
    }
}

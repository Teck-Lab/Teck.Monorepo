using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.Extensions;
using Orders.Application.Database;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Orders.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class OrderMigrationModelTests(SharedTestcontainersFixture fixture)
{
    private const string InitialOrderMigration = "20260629171802_InitialOrder";

    [Fact]
    public async Task CommittedMigration_AppliesWithoutPendingMigrationsOrModelChanges()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(OrderDbContext), "Order.Host");
        await using var context = CreateContext(connectionString);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task LifecycleMigration_UpgradesLegacyOrdersWithoutChangingTheirStoredStatusMeaning()
    {
        var databaseName = $"testdb_order_upgrade_{Guid.NewGuid():N}";
        var connectionString = fixture.GetDatabaseConnectionString(databaseName);
        var legacyOrders = new[]
        {
            (Id: Guid.NewGuid(), Status: 3, Total: 10m, ExpectedStatus: OrderStatus.Shipped),
            (Id: Guid.NewGuid(), Status: 4, Total: 20m, ExpectedStatus: OrderStatus.Delivered),
            (Id: Guid.NewGuid(), Status: 5, Total: 30m, ExpectedStatus: OrderStatus.Cancelled),
        };

        try
        {
            await CreateDatabaseAsync(databaseName);
            await using var context = CreateContext(connectionString);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(InitialOrderMigration);

            foreach (var legacyOrder in legacyOrders)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO "Orders" ("Id", "CustomerId", "TenantId", "Status", "Total", "CreatedAt", "IsDeleted")
                    VALUES ({legacyOrder.Id}, {Guid.NewGuid()}, {"legacy-tenant"}, {legacyOrder.Status}, {legacyOrder.Total}, {DateTimeOffset.UtcNow}, {false})
                    """);
            }

            await migrator.MigrateAsync();
            context.ChangeTracker.Clear();

            var upgradedOrders = await context.Orders
                .IgnoreQueryFilters([Constants.TenantToken])
                .OrderBy(order => order.Id)
                .ToListAsync();

            Assert.Equal(legacyOrders.Length, upgradedOrders.Count);
            foreach (var legacyOrder in legacyOrders)
            {
                var upgradedOrder = Assert.Single(upgradedOrders.Where(order => order.Id == legacyOrder.Id));
                Assert.Equal(legacyOrder.ExpectedStatus, upgradedOrder.Status);
                Assert.Equal(PaymentState.Pending, upgradedOrder.PaymentState);
                Assert.Equal(StockState.Pending, upgradedOrder.StockState);
                Assert.Equal(OrderFailureReason.None, upgradedOrder.FailureReason);
                Assert.Equal(string.Empty, upgradedOrder.ActionText);
                Assert.Equal(legacyOrder.Total, upgradedOrder.AuthorizedAmount);
                Assert.Equal("XXX", upgradedOrder.Currency);
                Assert.Equal($"legacy-unowned:{legacyOrder.Id}", upgradedOrder.KeycloakSubjectId);
                Assert.Equal($"legacy:{legacyOrder.Id:N}", upgradedOrder.CheckoutCorrelationId);
            }
        }
        finally
        {
            await fixture.DropTestDatabaseAsync(databaseName);
        }
    }

    private static OrderDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .UseTeckCloudTenant("order-migration-test")
            .Options;

        return new OrderDbContext(options, null!);
    }

    private async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(databaseName)}";
        await command.ExecuteNonQueryAsync();
    }
}

using Microsoft.EntityFrameworkCore;
using Notifications.Application.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Notifications.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class NotificationMigrationModelTests(SharedTestcontainersFixture fixture)
{
    internal const string FixtureTenantId = "tenant-a";

    [Fact]
    public async Task CommittedMigration_AppliesWithoutPendingMigrationsOrModelChanges()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(NotificationDbContext), "Notification.Host");
        await using var context = CreateContext(connectionString);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    internal static NotificationDbContext CreateContext(string connectionString, string tenantId = FixtureTenantId)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Notification.Host"))
            .UseTeckCloudTenant(tenantId);

        var options = optionsBuilder.Options;
        return new NotificationDbContext(options, null!);
    }
}

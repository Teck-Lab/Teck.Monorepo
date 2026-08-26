using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using Notifications.Host.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Notifications.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class TenantIsolationTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task WriteAndReadContexts_FilterContactsAndDeliveriesToTheirTenant()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Notifications.Application.Database.NotificationDbContext), "Notification.Host");
        var tenantADelivery = NotificationDelivery.Create("tenant-a", Guid.NewGuid(), Guid.NewGuid(), "subject-a", "tenant-a-event", "tenant-a-source", NotificationKind.OrderConfirmed, "Tenant A", "Tenant A delivery.", "tenant-a@example.test", null);
        var tenantBDelivery = NotificationDelivery.Create("tenant-b", Guid.NewGuid(), Guid.NewGuid(), "subject-b", "tenant-b-event", "tenant-b-source", NotificationKind.OrderCancelled, "Tenant B", "Tenant B delivery.", "tenant-b@example.test", null);

        await using (var seed = NotificationMigrationModelTests.CreateContext(connectionString, "tenant-a"))
        {
            seed.CustomerContacts.Add(CustomerContact.Create("tenant-a", tenantADelivery.CustomerId!.Value, "subject-a", "tenant-a@example.test"));
            seed.NotificationDeliveries.Add(tenantADelivery);
            await seed.SaveChangesAsync();
        }

        await using (var seed = NotificationMigrationModelTests.CreateContext(connectionString, "tenant-b"))
        {
            seed.CustomerContacts.Add(CustomerContact.Create("tenant-b", tenantBDelivery.CustomerId!.Value, "subject-b", "tenant-b@example.test"));
            seed.NotificationDeliveries.Add(tenantBDelivery);
            await seed.SaveChangesAsync();
        }

        await using var writeTenantA = NotificationMigrationModelTests.CreateContext(connectionString, "tenant-a");
        await using var writeTenantB = NotificationMigrationModelTests.CreateContext(connectionString, "tenant-b");
        await using var readTenantA = CreateReadContext(connectionString, "tenant-a");
        await using var readTenantB = CreateReadContext(connectionString, "tenant-b");

        await AssertFilteredToTenantAsync(writeTenantA.CustomerContacts, writeTenantA.NotificationDeliveries, "tenant-a", tenantADelivery.Id, tenantBDelivery.Id);
        await AssertFilteredToTenantAsync(writeTenantB.CustomerContacts, writeTenantB.NotificationDeliveries, "tenant-b", tenantBDelivery.Id, tenantADelivery.Id);
        await AssertFilteredToTenantAsync(readTenantA.CustomerContacts, readTenantA.NotificationDeliveries, "tenant-a", tenantADelivery.Id, tenantBDelivery.Id);
        await AssertFilteredToTenantAsync(readTenantB.CustomerContacts, readTenantB.NotificationDeliveries, "tenant-b", tenantBDelivery.Id, tenantADelivery.Id);
    }

    private static NotificationReadDbContext CreateReadContext(string connectionString, string tenantId)
    {
        var options = new DbContextOptionsBuilder<NotificationReadDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(tenantId)
            .Options;
        return new NotificationReadDbContext(options, null!);
    }

    private static async Task AssertFilteredToTenantAsync(DbSet<CustomerContact> contacts, DbSet<NotificationDelivery> deliveries, string tenantId, Guid visibleDeliveryId, Guid hiddenDeliveryId)
    {
        var visibleContacts = await contacts.ToListAsync().ConfigureAwait(false);
        var visibleDeliveries = await deliveries.ToListAsync().ConfigureAwait(false);

        Assert.NotEmpty(visibleContacts);
        Assert.All(visibleContacts, contact => Assert.Equal(tenantId, contact.TenantId));
        Assert.Contains(visibleDeliveries, delivery => delivery.Id == visibleDeliveryId);
        Assert.DoesNotContain(visibleDeliveries, delivery => delivery.Id == hiddenDeliveryId);
        Assert.All(visibleDeliveries, delivery => Assert.Equal(tenantId, delivery.TenantId));
    }
}

using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Notifications.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class NotificationDeliveryTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task PersistedDelivery_RetainsRecipientTemplateAndSourceCorrelation()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Notifications.Application.Database.NotificationDbContext), "Notification.Host");
        var delivery = NotificationDelivery.Create("tenant-a", Guid.NewGuid(), Guid.NewGuid(), "subject-a", "event-a", "source-a", NotificationKind.OrderConfirmed, "Your order is confirmed", "Your order is confirmed.", "shopper@example.test", null);
        await using (var write = NotificationMigrationModelTests.CreateContext(connectionString))
        {
            write.NotificationDeliveries.Add(delivery);
            await write.SaveChangesAsync();
        }

        await using var read = NotificationMigrationModelTests.CreateContext(connectionString);
        var persisted = await read.NotificationDeliveries.SingleAsync(item => item.Id == delivery.Id);
        Assert.Equal("shopper@example.test", persisted.Recipient);
        Assert.Equal(NotificationKind.OrderConfirmed, persisted.Kind);
        Assert.Equal("Your order is confirmed", persisted.Subject);
        Assert.Equal("Your order is confirmed.", persisted.Body);
        Assert.Equal("source-a", persisted.SourceCorrelationId);
        Assert.Equal("tenant-a", persisted.TenantId);
    }
}

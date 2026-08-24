using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Database;
using Notifications.Application.Notifications;
using Notifications.Application.Notifications.Features.SendEmail.V1;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using Notifications.Host.Database;
using Notifications.Host.Infrastructure;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Notifications.IntegrationTests;

/// <summary>Proves that deterministic stub acceptance shares a durable transaction with delivery state.</summary>
[Collection("SharedTestcontainers")]
public sealed class StubEmailSenderDurabilityTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task Handle_AfterRestart_LeavesOneDurableAcceptanceAndOneSentDelivery()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(NotificationDbContext), "Notification.Host");
        var delivery = await SeedDeliveryAsync(connectionString, "restart-key");

        await SendAsync(connectionString, delivery.Id);
        await SendAsync(connectionString, delivery.Id);

        await using var verify = NotificationMigrationModelTests.CreateContext(connectionString);
        Assert.Single(await verify.StubEmailAcceptances.IgnoreQueryFilters().Where(receipt => receipt.IdempotencyKey == "restart-key").ToListAsync());
        Assert.Equal(DeliveryStatus.Sent, (await verify.NotificationDeliveries.IgnoreQueryFilters().SingleAsync(item => item.Id == delivery.Id)).Status);
    }

    [Fact]
    public async Task Handle_ConcurrentCommands_LeaveOneDurableAcceptanceAndOneSentDelivery()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(NotificationDbContext), "Notification.Host");
        var delivery = await SeedDeliveryAsync(connectionString, "concurrent-key");

        await Task.WhenAll(SendAsync(connectionString, delivery.Id), SendAsync(connectionString, delivery.Id));

        await using var verify = NotificationMigrationModelTests.CreateContext(connectionString);
        Assert.Single(await verify.StubEmailAcceptances.IgnoreQueryFilters().Where(receipt => receipt.IdempotencyKey == "concurrent-key").ToListAsync());
        Assert.Equal(DeliveryStatus.Sent, (await verify.NotificationDeliveries.IgnoreQueryFilters().SingleAsync(item => item.Id == delivery.Id)).Status);
    }

    private static async Task<NotificationDelivery> SeedDeliveryAsync(string connectionString, string key)
    {
        var delivery = NotificationDelivery.Create("tenant-a", Guid.NewGuid(), Guid.NewGuid(), "subject-a", key, $"source:{key}", NotificationKind.OrderConfirmed, "Your order is confirmed", "Your order is confirmed.", "shopper@example.test", null);
        await using var seed = NotificationMigrationModelTests.CreateContext(connectionString);
        seed.NotificationDeliveries.Add(delivery);
        await seed.SaveChangesAsync();
        return delivery;
    }

    private static async Task SendAsync(string connectionString, Guid deliveryId)
    {
        await using var context = NotificationMigrationModelTests.CreateContext(connectionString, "tenant-a");
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var deliveries = new NotificationWriteRepository<NotificationDelivery, Guid>(context, accessor);
        using var unitOfWork = new UnitOfWork<NotificationDbContext>(context);
        var sender = new StubEmailSender(new StubEmailAcceptanceDbContextStore(context));

        await SendEmailHandler.Handle(new SendEmailCommand(deliveryId), deliveries, unitOfWork, sender, CancellationToken.None);
    }
}

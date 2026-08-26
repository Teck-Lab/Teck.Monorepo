using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Database;
using Notifications.Application.Notifications;
using Notifications.Application.Notifications.EventHandlers.IntegrationEvents;
using Notifications.Application.Notifications.Features.QueueNotification.V1;
using Notifications.Application.Notifications.Features.SendEmail.V1;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using Notifications.Host.Database;
using Notifications.Host.Infrastructure;
using NSubstitute;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace Notifications.IntegrationTests;

/// <summary>Integration coverage for customer-contact reconciliation and durable notification delivery.</summary>
[Collection("SharedTestcontainers")]
public sealed class CustomerContactReconciliationTests(SharedTestcontainersFixture fixture)
{
    /// <summary>Uses the subject-keyed contact when an order confirmation carries an absent customer correlation.</summary>
    [Fact]
    public async Task OrderConfirmation_WithEmptyCustomerId_ResolvesTheSubjectKeyedContact()
    {
        const string tenantId = "tenant-subject-contact";
        const string subjectId = "subject-contact";
        const string email = "subject-contact@example.test";
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(NotificationDbContext), "Notification.Host");
        await using (var seed = NotificationMigrationModelTests.CreateContext(connectionString, tenantId))
        {
            seed.CustomerContacts.Add(CustomerContact.Create(tenantId, Guid.NewGuid(), subjectId, email));
            await seed.SaveChangesAsync();
        }

        var orderBus = Substitute.For<IMessageBus>();
        QueueNotificationCommand? queued = null;
        orderBus.InvokeAsync(Arg.Do<QueueNotificationCommand>(command => queued = command), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var orderEvent = new OrderConfirmedIntegrationEvent
        {
            CustomerId = Guid.Empty,
            OrderId = Guid.NewGuid(),
            KeycloakSubjectId = subjectId,
            TenantId = tenantId,
            Amount = 42.00m,
            Currency = "USD",
            IdempotencyKey = "order:subject-contact",
            SourceCorrelationId = "order:subject-contact",
        };

        await OrderConfirmedHandler.Handle(orderEvent, orderBus, CancellationToken.None);

        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(tenantId);
        var queueBus = Substitute.For<IMessageBus>();
        var deliveryId = await QueueAsync(connectionString, Assert.IsType<QueueNotificationCommand>(queued), tenant, queueBus);

        await using var verify = NotificationMigrationModelTests.CreateContext(connectionString, tenantId);
        var delivery = await verify.NotificationDeliveries.SingleAsync(item => item.Id == deliveryId);
        Assert.Equal(email, delivery.Recipient);
        Assert.Null(delivery.ContactRequestId);
        await queueBus.DidNotReceive().PublishAsync(Arg.Any<CustomerContactReconciliationRequestedIntegrationEvent>());
    }

    /// <summary>Stores a pending delivery, reconciles it once, and keeps redelivery idempotent.</summary>
    [Fact]
    public async Task MissingContact_ReconciledResponse_ResumesExactlyOnePersistedDelivery()
    {
        const string tenantId = "tenant-reconciliation";
        const string subjectId = "subject-reconciliation";
        const string email = "reconciled@example.test";
        const string idempotencyKey = "order:reconciliation";
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(NotificationDbContext), "Notification.Host");
        var resolvedCustomerId = Guid.NewGuid();
        var orderEvent = new OrderConfirmedIntegrationEvent
        {
            CustomerId = null,
            OrderId = Guid.NewGuid(),
            KeycloakSubjectId = subjectId,
            TenantId = tenantId,
            Amount = 42.00m,
            Currency = "USD",
            IdempotencyKey = idempotencyKey,
            SourceCorrelationId = "order:reconciliation",
        };
        var orderBus = Substitute.For<IMessageBus>();
        QueueNotificationCommand? queued = null;
        orderBus.InvokeAsync(Arg.Do<QueueNotificationCommand>(command => queued = command), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await OrderConfirmedHandler.Handle(orderEvent, orderBus, CancellationToken.None);

        var queueCommand = Assert.IsType<QueueNotificationCommand>(queued);
        var reconciliationBus = Substitute.For<IMessageBus>();
        CustomerContactReconciliationRequestedIntegrationEvent? request = null;
        reconciliationBus.PublishAsync(Arg.Do<CustomerContactReconciliationRequestedIntegrationEvent>(evt => request = evt))
            .Returns(ValueTask.CompletedTask);
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(tenantId);

        var deliveryId = await QueueAsync(connectionString, queueCommand, tenant, reconciliationBus);

        await using (var pendingContext = NotificationMigrationModelTests.CreateContext(connectionString, tenantId))
        {
            var pending = await pendingContext.NotificationDeliveries.SingleAsync(item => item.Id == deliveryId);
            Assert.Single(await pendingContext.NotificationDeliveries.Where(item => item.IdempotencyKey == idempotencyKey && item.Status == DeliveryStatus.Pending).ToListAsync());
            Assert.Equal(DeliveryStatus.Pending, pending.Status);
            Assert.Null(pending.Recipient);
            Assert.Equal(Assert.IsType<CustomerContactReconciliationRequestedIntegrationEvent>(request).RequestId, pending.ContactRequestId);
            Assert.DoesNotContain(
                await pendingContext.CustomerContacts.ToListAsync(),
                contact => contact.KeycloakSubjectId == subjectId);
        }

        await reconciliationBus.Received(1).PublishAsync(Arg.Any<CustomerContactReconciliationRequestedIntegrationEvent>());
        var response = new CustomerContactReconciledIntegrationEvent
        {
            CustomerId = resolvedCustomerId,
            KeycloakSubjectId = subjectId,
            TenantId = tenantId,
            Email = email,
            RequestId = request!.RequestId,
            SourceCorrelationId = orderEvent.SourceCorrelationId,
        };
        var dispatchBus = Substitute.For<IMessageBus>();
        SendEmailCommand? send = null;
        dispatchBus.InvokeAsync(Arg.Do<SendEmailCommand>(command => send = command), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await ReconcileAsync(connectionString, response, dispatchBus);
        await SendAsync(connectionString, Assert.IsType<SendEmailCommand>(send).DeliveryId, tenantId);
        await ReconcileAsync(connectionString, response, dispatchBus);

        await dispatchBus.Received(1).InvokeAsync(Arg.Is<SendEmailCommand>(command => command.DeliveryId == deliveryId), Arg.Any<CancellationToken>());
        await using var verify = NotificationMigrationModelTests.CreateContext(connectionString, tenantId);
        Assert.Single(await verify.CustomerContacts.Where(contact => contact.CustomerId == resolvedCustomerId).ToListAsync());
        Assert.Single(await verify.StubEmailAcceptances.Where(receipt => receipt.IdempotencyKey == idempotencyKey).ToListAsync());
        var delivery = await verify.NotificationDeliveries.SingleAsync(item => item.Id == deliveryId);
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.Equal(email, delivery.Recipient);
        Assert.Single(await verify.NotificationDeliveries.Where(item => item.IdempotencyKey == idempotencyKey && item.Status == DeliveryStatus.Sent).ToListAsync());
    }

    private static async Task<Guid> QueueAsync(string connectionString, QueueNotificationCommand command, ITenantInfo tenant, IMessageBus bus)
    {
        await using var writeContext = NotificationMigrationModelTests.CreateContext(connectionString, tenant.Id);
        await using var readContext = CreateReadContext(connectionString, tenant.Id!);
        var deliveries = new NotificationWriteRepository<NotificationDelivery, Guid>(writeContext, new HttpContextAccessor());
        var contacts = new NotificationReadRepository<CustomerContact, Guid>(readContext);
        using var unitOfWork = new UnitOfWork<NotificationDbContext>(writeContext);
        return await QueueNotificationHandler.Handle(command, deliveries, contacts, unitOfWork, tenant, bus, CancellationToken.None);
    }

    private static async Task ReconcileAsync(string connectionString, CustomerContactReconciledIntegrationEvent response, IMessageBus bus)
    {
        await using var context = NotificationMigrationModelTests.CreateContext(connectionString, response.TenantId);
        var contacts = new NotificationWriteRepository<CustomerContact, Guid>(context, new HttpContextAccessor());
        var deliveries = new NotificationWriteRepository<NotificationDelivery, Guid>(context, new HttpContextAccessor());
        using var unitOfWork = new UnitOfWork<NotificationDbContext>(context);
        await CustomerContactReconciledHandler.Handle(response, contacts, deliveries, unitOfWork, bus, CancellationToken.None);
    }

    private static async Task SendAsync(string connectionString, Guid deliveryId, string tenantId)
    {
        await using var context = NotificationMigrationModelTests.CreateContext(connectionString, tenantId);
        var deliveries = new NotificationWriteRepository<NotificationDelivery, Guid>(context, new HttpContextAccessor());
        using var unitOfWork = new UnitOfWork<NotificationDbContext>(context);
        await SendEmailHandler.Handle(new SendEmailCommand(deliveryId), deliveries, unitOfWork, new StubEmailSender(new StubEmailAcceptanceDbContextStore(context)), CancellationToken.None);
    }

    private static NotificationReadDbContext CreateReadContext(string connectionString, string tenantId)
    {
        var options = new DbContextOptionsBuilder<NotificationReadDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(tenantId)
            .Options;
        return new NotificationReadDbContext(options, null!);
    }
}

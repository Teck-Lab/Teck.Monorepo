using Ardalis.Specification;
using Notifications.Application.Notifications.EventHandlers.IntegrationEvents;
using Notifications.Application.Notifications.Features.QueueNotification.V1;
using Notifications.Application.Notifications.Features.SendEmail.V1;
using Notifications.Application.Notifications;
using Notifications.Application.Notifications.ReadModels;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Finbuckle.MultiTenant.Abstractions;
using Xunit;

namespace Notifications.UnitTests;

public sealed class CustomerContactProjectionTests
{
    [Fact]
    public async Task MissingContact_ReconcilesThroughSharedEventsAndDispatchesExactlyOnce()
    {
        var deliveries = Substitute.For<IGenericWriteRepository<NotificationDelivery, Guid>>();
        var contactReader = Substitute.For<IGenericReadRepository<CustomerContact, Guid>>();
        var contacts = Substitute.For<IGenericWriteRepository<CustomerContact, Guid>>();
        var queueUnitOfWork = Substitute.For<IUnitOfWork>();
        var reconciliationUnitOfWork = Substitute.For<IUnitOfWork>();
        var sendUnitOfWork = Substitute.For<IUnitOfWork>();
        var tenant = Substitute.For<ITenantInfo>();
        var bus = Substitute.For<IMessageBus>();
        var sender = Substitute.For<IEmailSender>();
        NotificationDelivery? delivery = null;
        CustomerContact? contact = null;
        CustomerContactReconciliationRequestedIntegrationEvent? request = null;
        contactReader.FirstOrDefaultAsync(Arg.Any<ISpecification<CustomerContact>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerContact?>(null));
        deliveries.FirstOrDefaultAsync(Arg.Any<ISpecification<NotificationDelivery>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<ISpecification<NotificationDelivery>>() is DeliveryByIdSpec ? delivery : null));
        deliveries.FirstOrDefaultAsync(Arg.Any<ISpecification<NotificationDelivery>>(), true, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(delivery));
        deliveries.AddAsync(Arg.Any<NotificationDelivery>(), Arg.Any<CancellationToken>())
            .Returns(call => { delivery = call.Arg<NotificationDelivery>(); return Task.CompletedTask; });
        contacts.FirstOrDefaultAsync(Arg.Any<ISpecification<CustomerContact>>(), true, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(contact));
        contacts.AddAsync(Arg.Any<CustomerContact>(), Arg.Any<CancellationToken>())
            .Returns(call => { contact = call.Arg<CustomerContact>(); return Task.CompletedTask; });
        deliveries.ListAsync(Arg.Any<ISpecification<NotificationDelivery>>(), true, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<NotificationDelivery>>(delivery is null ? [] : [delivery]));
        bus.PublishAsync(Arg.Do<CustomerContactReconciliationRequestedIntegrationEvent>(value => request = value));
        tenant.Id.Returns("tenant-a");
        var command = new QueueNotificationCommand(Guid.NewGuid(), Guid.NewGuid(), "subject-a", "tenant-a", "order:reconcile", "source:reconcile", NotificationKind.OrderConfirmed, "Your order is confirmed", "Your order is confirmed.");

        Guid deliveryId = await QueueNotificationHandler.Handle(command, deliveries, contactReader, queueUnitOfWork, tenant, bus, CancellationToken.None);

        var response = new CustomerContactReconciledIntegrationEvent { CustomerId = command.CustomerId!.Value, KeycloakSubjectId = command.KeycloakSubjectId, TenantId = command.TenantId, Email = "shopper@example.test", RequestId = Assert.IsType<CustomerContactReconciliationRequestedIntegrationEvent>(request).RequestId, SourceCorrelationId = command.SourceCorrelationId };
        await CustomerContactReconciledHandler.Handle(response, contacts, deliveries, reconciliationUnitOfWork, bus, CancellationToken.None);
        await SendEmailHandler.Handle(new SendEmailCommand(deliveryId), deliveries, sendUnitOfWork, sender, CancellationToken.None);
        await SendEmailHandler.Handle(new SendEmailCommand(deliveryId), deliveries, sendUnitOfWork, sender, CancellationToken.None);

        Assert.NotNull(contact);
        Assert.Equal("tenant-a", contact!.TenantId);
        Assert.NotNull(delivery);
        Assert.Equal("shopper@example.test", delivery!.Recipient);
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(message => message.Recipient == "shopper@example.test"),
            command.TenantId,
            command.IdempotencyKey,
            Arg.Any<CancellationToken>());
        await deliveries.Received(1).AddAsync(Arg.Any<NotificationDelivery>(), Arg.Any<CancellationToken>());
        await contacts.Received(1).AddAsync(Arg.Any<CustomerContact>(), Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<CustomerContactReconciliationRequestedIntegrationEvent>());
        await bus.Received(1).InvokeAsync(Arg.Is<SendEmailCommand>(sent => sent.DeliveryId == deliveryId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CustomerCreated_RedeliveryUpsertsTheExistingTenantScopedContact()
    {
        var contacts = Substitute.For<IGenericWriteRepository<CustomerContact, Guid>>();
        var deliveries = Substitute.For<IGenericWriteRepository<NotificationDelivery, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        CustomerContact? stored = null;
        contacts.FirstOrDefaultAsync(Arg.Any<ISpecification<CustomerContact>>(), true, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(stored));
        contacts.AddAsync(Arg.Any<CustomerContact>(), Arg.Any<CancellationToken>())
            .Returns(call => { stored = call.Arg<CustomerContact>(); return Task.CompletedTask; });
        deliveries.ListAsync(Arg.Any<ISpecification<NotificationDelivery>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<NotificationDelivery>>([]));
        var evt = new CustomerCreatedIntegrationEvent { CustomerId = Guid.NewGuid(), TenantId = "tenant-a", KeycloakSubjectId = "subject-a", Email = "shopper@example.test" };

        await CustomerCreatedHandler.Handle(evt, contacts, deliveries, unitOfWork, bus, CancellationToken.None);
        await CustomerCreatedHandler.Handle(evt, contacts, deliveries, unitOfWork, bus, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("tenant-a", stored!.TenantId);
        Assert.Equal("subject-a", stored.KeycloakSubjectId);
        Assert.Equal("shopper@example.test", stored.Email);
        await contacts.Received(1).AddAsync(Arg.Any<CustomerContact>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

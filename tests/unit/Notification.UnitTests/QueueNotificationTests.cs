using Ardalis.Specification;
using Finbuckle.MultiTenant.Abstractions;
using Notifications.Application.Notifications.Features.QueueNotification.V1;
using Notifications.Application.Notifications.Features.SendEmail.V1;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using Wolverine;
using Xunit;

namespace Notifications.UnitTests;

public sealed class QueueNotificationTests
{
    [Fact]
    public async Task Handle_RepeatedIdempotencyKey_PersistsOnceAndResumesDispatch()
    {
        var deliveries = Substitute.For<IGenericWriteRepository<NotificationDelivery, Guid>>();
        NotificationDelivery? stored = null;
        deliveries.FirstOrDefaultAsync(Arg.Any<ISpecification<NotificationDelivery>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(stored));
        deliveries.AddAsync(Arg.Any<NotificationDelivery>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => { stored = callInfo.Arg<NotificationDelivery>(); return Task.CompletedTask; });

        var contacts = Substitute.For<IGenericReadRepository<CustomerContact, Guid>>();
        contacts.FirstOrDefaultAsync(Arg.Any<ISpecification<CustomerContact>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerContact?>(CustomerContact.Create("tenant-a", Guid.NewGuid(), "subject-a", "shopper@example.test")));
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-a");
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        var command = new QueueNotificationCommand(Guid.NewGuid(), Guid.NewGuid(), "subject-a", "tenant-a", "order:42:rejected", "event:42", NotificationKind.OrderRejected, "Your order could not be completed", "Your order could not be completed. Please try another payment method.");

        Guid first = await QueueNotificationHandler.Handle(command, deliveries, contacts, unitOfWork, tenant, bus, CancellationToken.None);
        Guid repeated = await QueueNotificationHandler.Handle(command, deliveries, contacts, unitOfWork, tenant, bus, CancellationToken.None);

        Assert.Equal(first, repeated);
        Assert.NotNull(stored);
        Assert.Equal("shopper@example.test", stored!.Recipient);
        Assert.Equal(NotificationKind.OrderRejected, stored.Kind);
        Assert.Equal("event:42", stored.SourceCorrelationId);
        await deliveries.Received(1).AddAsync(Arg.Any<NotificationDelivery>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(2).InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RedeliveryAfterPostCommitDispatchFailure_ResumesThePendingDelivery()
    {
        var deliveries = Substitute.For<IGenericWriteRepository<NotificationDelivery, Guid>>();
        NotificationDelivery? stored = null;
        deliveries.FirstOrDefaultAsync(Arg.Any<ISpecification<NotificationDelivery>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(stored));
        deliveries.AddAsync(Arg.Any<NotificationDelivery>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => { stored = callInfo.Arg<NotificationDelivery>(); return Task.CompletedTask; });

        var contacts = Substitute.For<IGenericReadRepository<CustomerContact, Guid>>();
        contacts.FirstOrDefaultAsync(Arg.Any<ISpecification<CustomerContact>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerContact?>(CustomerContact.Create("tenant-a", Guid.NewGuid(), "subject-a", "shopper@example.test")));
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-a");
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        bus.InvokeAsync(Arg.Any<SendEmailCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<object?>(new InvalidOperationException("post-commit dispatch failure")), _ => Task.FromResult<object?>(null));
        var command = new QueueNotificationCommand(Guid.NewGuid(), Guid.NewGuid(), "subject-a", "tenant-a", "order:42:confirmed", "event:42", NotificationKind.OrderConfirmed, "Your order is confirmed", "Your order is confirmed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => QueueNotificationHandler.Handle(command, deliveries, contacts, unitOfWork, tenant, bus, CancellationToken.None));
        Guid resumed = await QueueNotificationHandler.Handle(command, deliveries, contacts, unitOfWork, tenant, bus, CancellationToken.None);

        Assert.Equal(stored!.Id, resumed);
        await deliveries.Received(1).AddAsync(Arg.Any<NotificationDelivery>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(2).InvokeAsync(Arg.Is<SendEmailCommand>(send => send.DeliveryId == stored.Id), Arg.Any<CancellationToken>());
    }
}

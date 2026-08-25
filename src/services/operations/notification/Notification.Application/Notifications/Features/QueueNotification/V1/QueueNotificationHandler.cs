using Finbuckle.MultiTenant.Abstractions;
using Notifications.Application.Notifications.Features.SendEmail.V1;
using Notifications.Application.Notifications.ReadModels;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Notifications.Application.Notifications.Features.QueueNotification.V1;

/// <summary>Persists a notification before asynchronously requesting contact resolution or dispatch.</summary>
public static class QueueNotificationHandler
{
    /// <summary>Creates one delivery for the supplied stable idempotency key.</summary>
    /// <param name="command">The rendered notification to queue.</param>
    /// <param name="deliveries">The delivery write repository.</param>
    /// <param name="contacts">The customer contact read repository.</param>
    /// <param name="unitOfWork">The unit of work used to persist the delivery.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="bus">The message bus used for follow-up processing.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The persisted delivery identifier.</returns>
    public static async Task<Guid> Handle(QueueNotificationCommand command, IGenericWriteRepository<NotificationDelivery, Guid> deliveries, IGenericReadRepository<CustomerContact, Guid> contacts, IUnitOfWork unitOfWork, ITenantInfo tenant, IMessageBus bus, CancellationToken ct)
    {
        var existing = await deliveries.FirstOrDefaultAsync(new DeliveryByIdempotencyKeySpec(command.IdempotencyKey), ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await ResumeDispatchAsync(existing, command, bus, ct).ConfigureAwait(false);
            return existing.Id;
        }

        var customerId = CustomerIdOrNull(command.CustomerId);
        CustomerContact? contact = customerId is Guid resolvedCustomerId
            ? await contacts.FirstOrDefaultAsync(new CustomerContactByCustomerSpec(resolvedCustomerId), ct).ConfigureAwait(false)
            : await contacts.FirstOrDefaultAsync(new CustomerContactBySubjectSpec(command.KeycloakSubjectId), ct).ConfigureAwait(false);
        var requestId = contact is null ? $"contact:{command.TenantId}:{customerId}:{command.KeycloakSubjectId}" : null;
        var delivery = NotificationDelivery.Create(tenant.Id ?? command.TenantId, customerId, command.OrderId, command.KeycloakSubjectId, command.IdempotencyKey, command.SourceCorrelationId, command.Kind, command.Subject, command.Body, contact?.Email, requestId);
        await deliveries.AddAsync(delivery, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await ResumeDispatchAsync(delivery, command, bus, ct).ConfigureAwait(false);

        return delivery.Id;
    }

    private static Task ResumeDispatchAsync(NotificationDelivery delivery, QueueNotificationCommand command, IMessageBus bus, CancellationToken ct)
    {
        if (delivery.Status == DeliveryStatus.Sent)
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(delivery.Recipient))
        {
            var customerId = CustomerIdOrNull(delivery.CustomerId ?? command.CustomerId);
            var requestId = delivery.ContactRequestId ?? $"contact:{command.TenantId}:{customerId}:{command.KeycloakSubjectId}";
            return bus.PublishAsync(new CustomerContactReconciliationRequestedIntegrationEvent { CustomerId = customerId ?? Guid.Empty, KeycloakSubjectId = command.KeycloakSubjectId, TenantId = command.TenantId, RequestId = requestId, SourceCorrelationId = command.SourceCorrelationId }).AsTask();
        }

        return bus.InvokeAsync(new SendEmailCommand(delivery.Id), ct);
    }

    private static Guid? CustomerIdOrNull(Guid? customerId) => customerId is { } value && value != Guid.Empty ? value : null;
}

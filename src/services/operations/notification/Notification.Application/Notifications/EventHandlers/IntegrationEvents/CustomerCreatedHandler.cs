using Notifications.Application.Notifications.Features.SendEmail.V1;
using Notifications.Application.Notifications.ReadModels;
using Notifications.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Notifications.Application.Notifications.EventHandlers.IntegrationEvents;

/// <summary>Maintains notification contacts from forward customer-created events.</summary>
public static class CustomerCreatedHandler
{
    /// <summary>Idempotently upserts a customer contact and resumes matching pending deliveries.</summary>
    /// <param name="evt">The customer-created integration event.</param>
    /// <param name="contacts">The customer contact write repository.</param>
    /// <param name="deliveries">The notification delivery write repository.</param>
    /// <param name="unitOfWork">The unit of work used to persist changes.</param>
    /// <param name="bus">The message bus used to invoke waiting deliveries.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after contacts and waiting deliveries are updated.</returns>
    public static async Task Handle(CustomerCreatedIntegrationEvent evt, IGenericWriteRepository<CustomerContact, Guid> contacts, IGenericWriteRepository<NotificationDelivery, Guid> deliveries, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var contact = await contacts.FirstOrDefaultAsync(new CustomerContactByCustomerSpec(evt.CustomerId), enableTracking: true, ct).ConfigureAwait(false);
        if (contact is null)
        {
            contact = CustomerContact.Create(evt.TenantId, evt.CustomerId, evt.KeycloakSubjectId, evt.Email);
            await contacts.AddAsync(contact, ct).ConfigureAwait(false);
        }
        else
        {
            contact.Update(evt.KeycloakSubjectId, evt.Email);
        }

        var requestId = $"contact:{evt.TenantId}:{evt.CustomerId}:{evt.KeycloakSubjectId}";
        var pending = await deliveries.ListAsync(new PendingDeliveriesByContactRequestSpec(requestId), enableTracking: true, ct).ConfigureAwait(false);
        foreach (var delivery in pending)
        {
            delivery.ResolveRecipient(evt.Email);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var delivery in pending)
        {
            await bus.InvokeAsync(new SendEmailCommand(delivery.Id), ct).ConfigureAwait(false);
        }
    }
}

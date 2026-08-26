using Notifications.Domain.ValueObjects;
using SharedKernel.Events;
using Wolverine;

namespace Notifications.Application.Notifications.EventHandlers.IntegrationEvents;

/// <summary>Queues the fixed backorder outcome notification.</summary>
public static class OrderBackorderOutcomeHandler
{
    /// <summary>Handles a shopper-safe backorder outcome.</summary>
    /// <param name="evt">The backorder-outcome integration event.</param>
    /// <param name="bus">The message bus used to queue the notification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the notification is queued.</returns>
    public static Task Handle(OrderBackorderOutcomeIntegrationEvent evt, IMessageBus bus, CancellationToken ct) => OrderNotificationTemplates.QueueAsync(evt.CustomerId, evt.OrderId, evt.KeycloakSubjectId, evt.TenantId, evt.IdempotencyKey, evt.SourceCorrelationId, NotificationKind.BackorderOutcome, "Update on your backordered order", "There is an update on your backordered order.", bus, ct);
}

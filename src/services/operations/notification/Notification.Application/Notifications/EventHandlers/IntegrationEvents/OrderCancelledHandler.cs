using Notifications.Domain.ValueObjects;
using SharedKernel.Events;
using Wolverine;

namespace Notifications.Application.Notifications.EventHandlers.IntegrationEvents;

/// <summary>Queues the fixed cancellation notification.</summary>
public static class OrderCancelledHandler
{
    /// <summary>Handles an order cancellation event.</summary>
    /// <param name="evt">The order-cancelled integration event.</param>
    /// <param name="bus">The message bus used to queue the notification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the notification is queued.</returns>
    public static Task Handle(OrderCancelledIntegrationEvent evt, IMessageBus bus, CancellationToken ct) => OrderNotificationTemplates.QueueAsync(evt.CustomerId, evt.OrderId, evt.KeycloakSubjectId, evt.TenantId, evt.IdempotencyKey, evt.SourceCorrelationId, NotificationKind.OrderCancelled, "Your order was cancelled", "Your order was cancelled.", bus, ct);
}

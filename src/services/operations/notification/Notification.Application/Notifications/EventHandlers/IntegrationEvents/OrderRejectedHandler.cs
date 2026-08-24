using Notifications.Domain.ValueObjects;
using SharedKernel.Events;
using Wolverine;

namespace Notifications.Application.Notifications.EventHandlers.IntegrationEvents;

/// <summary>Queues the fixed safe order-rejection notification.</summary>
public static class OrderRejectedHandler
{
    /// <summary>Handles an order rejection event without exposing provider detail.</summary>
    /// <param name="evt">The order-rejected integration event.</param>
    /// <param name="bus">The message bus used to queue the notification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the notification is queued.</returns>
    public static Task Handle(OrderRejectedIntegrationEvent evt, IMessageBus bus, CancellationToken ct) => OrderNotificationTemplates.QueueAsync(evt.CustomerId, evt.OrderId, evt.KeycloakSubjectId, evt.TenantId, evt.IdempotencyKey, evt.SourceCorrelationId, NotificationKind.OrderRejected, "Your order could not be completed", "Your order could not be completed. Please try another payment method.", bus, ct);
}

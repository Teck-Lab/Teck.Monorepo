using Notifications.Domain.ValueObjects;
using SharedKernel.Events;
using Wolverine;

namespace Notifications.Application.Notifications.EventHandlers.IntegrationEvents;

/// <summary>Queues the fixed order-confirmation notification.</summary>
public static class OrderConfirmedHandler
{
    /// <summary>Handles an order confirmation event.</summary>
    /// <param name="evt">The order-confirmed integration event.</param>
    /// <param name="bus">The message bus used to queue the notification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the notification is queued.</returns>
    public static Task Handle(OrderConfirmedIntegrationEvent evt, IMessageBus bus, CancellationToken ct) => OrderNotificationTemplates.QueueAsync(evt.CustomerId, evt.OrderId, evt.KeycloakSubjectId, evt.TenantId, evt.IdempotencyKey, evt.SourceCorrelationId, NotificationKind.OrderConfirmed, "Your order is confirmed", $"Your order {evt.OrderId} is confirmed for {evt.Amount:0.00} {evt.Currency}.", bus, ct);
}

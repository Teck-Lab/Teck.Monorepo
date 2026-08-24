using Notifications.Domain.ValueObjects;
using SharedKernel.Events;
using Wolverine;

namespace Notifications.Application.Notifications.EventHandlers.IntegrationEvents;

/// <summary>Queues the fixed payment-action notification.</summary>
public static class OrderPaymentActionRequiredHandler
{
    /// <summary>Handles a shopper-safe payment action event.</summary>
    /// <param name="evt">The payment-action-required integration event.</param>
    /// <param name="bus">The message bus used to queue the notification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the notification is queued.</returns>
    public static Task Handle(OrderPaymentActionRequiredIntegrationEvent evt, IMessageBus bus, CancellationToken ct) => OrderNotificationTemplates.QueueAsync(evt.CustomerId, evt.OrderId, evt.KeycloakSubjectId, evt.TenantId, evt.IdempotencyKey, evt.SourceCorrelationId, NotificationKind.PaymentActionRequired, "Action needed for your order", "Please update your payment method to continue your order.", bus, ct);
}

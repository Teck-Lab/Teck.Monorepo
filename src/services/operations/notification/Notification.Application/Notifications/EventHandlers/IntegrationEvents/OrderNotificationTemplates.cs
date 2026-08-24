using Notifications.Application.Notifications.Features.QueueNotification.V1;
using Notifications.Domain.ValueObjects;
using Wolverine;

namespace Notifications.Application.Notifications.EventHandlers.IntegrationEvents;

/// <summary>Maps shopper-safe order outcomes to fixed notification templates.</summary>
internal static class OrderNotificationTemplates
{
    internal static Task QueueAsync(Guid? customerId, Guid orderId, string subjectId, string tenantId, string key, string correlation, NotificationKind kind, string subject, string body, IMessageBus bus, CancellationToken ct) => bus.InvokeAsync(new QueueNotificationCommand(customerId, orderId, subjectId, tenantId, key, correlation, kind, subject, body), ct);
}

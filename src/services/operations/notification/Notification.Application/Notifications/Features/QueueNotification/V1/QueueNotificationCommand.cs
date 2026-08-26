using Notifications.Domain.ValueObjects;

namespace Notifications.Application.Notifications.Features.QueueNotification.V1;

/// <summary>Queues a rendered shopper notification for idempotent delivery.</summary>
/// <param name="CustomerId">The optional customer identifier for the recipient.</param>
/// <param name="OrderId">The source order identifier.</param>
/// <param name="KeycloakSubjectId">The immutable customer subject identifier.</param>
/// <param name="TenantId">The tenant that owns the delivery.</param>
/// <param name="IdempotencyKey">The stable key preventing duplicate deliveries.</param>
/// <param name="SourceCorrelationId">The source event correlation identifier.</param>
/// <param name="Kind">The notification template kind.</param>
/// <param name="Subject">The rendered subject.</param>
/// <param name="Body">The rendered body.</param>
public sealed record QueueNotificationCommand(Guid? CustomerId, Guid OrderId, string KeycloakSubjectId, string TenantId, string IdempotencyKey, string SourceCorrelationId, NotificationKind Kind, string Subject, string Body);

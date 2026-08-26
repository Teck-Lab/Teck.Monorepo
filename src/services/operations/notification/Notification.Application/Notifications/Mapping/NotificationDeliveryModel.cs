using Notifications.Domain.ValueObjects;

namespace Notifications.Application.Notifications.Mapping;

/// <summary>Immutable representation of every business-significant notification delivery field.</summary>
public sealed record NotificationDeliveryModel(
    Guid Id,
    string TenantId,
    Guid? CustomerId,
    Guid OrderId,
    string KeycloakSubjectId,
    string IdempotencyKey,
    string SourceCorrelationId,
    string? ContactRequestId,
    NotificationKind Kind,
    string? Recipient,
    string Subject,
    string Body,
    DeliveryStatus Status,
    DateTimeOffset? SentAt);

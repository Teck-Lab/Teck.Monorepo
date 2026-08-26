namespace Orders.Domain.DomainEvents;

/// <summary>Represents a one-time shopper-safe cancellation notification.</summary>
public sealed record OrderCancelled(Guid OrderId, Guid? CustomerId, string KeycloakSubjectId, string TenantId, string ActionText, string IdempotencyKey, string SourceCorrelationId);

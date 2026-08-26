namespace Orders.Domain.DomainEvents;

/// <summary>Represents a one-time shopper-safe rejection notification.</summary>
public sealed record OrderRejected(Guid OrderId, Guid? CustomerId, string KeycloakSubjectId, string TenantId, string FailureCategory, string ActionText, string IdempotencyKey, string SourceCorrelationId);

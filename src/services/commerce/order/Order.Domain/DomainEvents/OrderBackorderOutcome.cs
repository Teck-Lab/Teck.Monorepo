namespace Orders.Domain.DomainEvents;

/// <summary>Represents a shopper-safe backorder outcome notification.</summary>
public sealed record OrderBackorderOutcome(Guid OrderId, Guid? CustomerId, string KeycloakSubjectId, string TenantId, string Outcome, string ActionText, string IdempotencyKey, string SourceCorrelationId);

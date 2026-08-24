namespace Orders.Domain.DomainEvents;

/// <summary>Represents a one-time safe payment-action notification.</summary>
public sealed record OrderPaymentActionRequired(Guid OrderId, Guid? CustomerId, string KeycloakSubjectId, string TenantId, string DeclineCategory, string ActionText, string IdempotencyKey, string SourceCorrelationId);

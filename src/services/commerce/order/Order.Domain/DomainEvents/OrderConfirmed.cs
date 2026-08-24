namespace Orders.Domain.DomainEvents;

/// <summary>Represents a one-time confirmed-order shopper notification.</summary>
public sealed record OrderConfirmed(Guid OrderId, Guid? CustomerId, string KeycloakSubjectId, string TenantId, decimal Amount, string Currency, string IdempotencyKey, string SourceCorrelationId, decimal AuthorizedAmount);

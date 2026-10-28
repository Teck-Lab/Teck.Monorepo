namespace Billings.Application.Billing.Purchases;

/// <summary>Public immutable quote and lifecycle response.</summary>
public sealed record TenantPurchaseOrderDto(
    Guid Id,
    Guid TenantId,
    string TenantIdentifier,
    Guid SubscriptionId,
    string PlanId,
    string PlanVersion,
    long AmountMinor,
    string Currency,
    string DatabaseStrategy,
    string DatabaseProvider,
    bool HasReadReplicas,
    int ReplicaCount,
    string Status,
    string PaymentMethod,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? ValidUntilUtc,
    DateTimeOffset? CancelledAtUtc);

namespace Billings.Application.Billing.Purchases;

/// <summary>Requests cancellation of an already-paid order by its signed owner.</summary>
/// <param name="OrderId">The originating purchase order.</param>
/// <param name="OwnerSubjectId">The authenticated Keycloak subject.</param>
public sealed record CancelTenantPurchaseCommand(Guid OrderId, string OwnerSubjectId);

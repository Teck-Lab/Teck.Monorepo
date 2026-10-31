namespace Billings.Application.Billing.Purchases;

/// <summary>Fetches a purchase only for the Customer-verified reservation owner.</summary>
/// <param name="ReservationId">The Customer-issued reservation identifier.</param>
/// <param name="OwnerSubjectId">The signed Keycloak subject making the request.</param>
public sealed record GetTenantPurchaseOrderQuery(Guid ReservationId, string OwnerSubjectId);

namespace Billings.Host.Infrastructure;

/// <summary>Owner-bound Customer tenant reservation returned over the internal API.</summary>
public sealed class PurchaseReservationResponse
{
    /// <summary>Gets or sets the Customer tenant registry identifier.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the canonical tenant identifier.</summary>
    public string TenantIdentifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the Keycloak owner subject.</summary>
    public string OwnerSubjectId { get; set; } = string.Empty;

    /// <summary>Gets or sets the reservation state.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlated purchase order identifier.</summary>
    public Guid PurchaseOrderId { get; set; }
}

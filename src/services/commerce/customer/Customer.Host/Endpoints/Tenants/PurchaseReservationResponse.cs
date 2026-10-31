namespace Customers.Host.Endpoints.Tenants;

/// <summary>Authenticated Customer tenant purchase reservation.</summary>
public sealed class PurchaseReservationResponse
{
    /// <summary>Gets or sets the Customer registry identifier.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the canonical tenant slug.</summary>
    public string TenantIdentifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the Keycloak subject owning the reservation.</summary>
    public string OwnerSubjectId { get; set; } = string.Empty;

    /// <summary>Gets or sets the reservation lifecycle state.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the purchase order correlated with the tenant, if any.</summary>
    public Guid PurchaseOrderId { get; set; }
}

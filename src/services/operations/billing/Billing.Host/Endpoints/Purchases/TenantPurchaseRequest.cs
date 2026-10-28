namespace Billings.Host.Endpoints.Purchases;

/// <summary>Tenant identifier passed to purchase endpoints.</summary>
public sealed class TenantPurchaseRequest
{
    /// <summary>Gets or sets the tenant registry identifier.</summary>
    public Guid TenantId { get; set; }
}

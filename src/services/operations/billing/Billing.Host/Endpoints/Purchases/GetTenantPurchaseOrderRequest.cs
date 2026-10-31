namespace Billings.Host.Endpoints.Purchases;

/// <summary>Authenticated tenant identifier for purchase status lookup.</summary>
public sealed class GetTenantPurchaseOrderRequest
{
    /// <summary>Gets or sets the Customer-issued reservation identifier.</summary>
    public Guid ReservationId { get; set; }
}

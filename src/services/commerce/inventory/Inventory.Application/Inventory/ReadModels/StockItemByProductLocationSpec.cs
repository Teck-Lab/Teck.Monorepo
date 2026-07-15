using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>Selects the single stock record for a tenant's product at a specific location.</summary>
/// <remarks>
/// Tenant-scoped explicitly (rather than relying on an ambient Finbuckle tenant filter) so this
/// spec is safe to use from contexts with no per-request tenant, such as the expiry sweep (Task
/// 18), which loads the <see cref="StockItem"/> backing each reservation allocation directly by
/// its owning tenant. <c>IgnoreQueryFilters()</c> is applied for the same reason — see
/// <see cref="ExpiredHeldReservationsSpec"/> for the full rationale.
/// </remarks>
public sealed class StockItemByProductLocationSpec : Specification<StockItem>
{
    /// <summary>Initializes a new instance of the <see cref="StockItemByProductLocationSpec"/> class.</summary>
    /// <param name="tenantId">The owning tenant identifier to match.</param>
    /// <param name="productId">The product identifier to match.</param>
    /// <param name="locationId">The location identifier to match.</param>
    public StockItemByProductLocationSpec(string tenantId, Guid productId, Guid locationId)
    {
        Query
            .Where(item => item.TenantId == tenantId && item.ProductId == productId && item.LocationId == locationId)
            .IgnoreQueryFilters();
    }
}

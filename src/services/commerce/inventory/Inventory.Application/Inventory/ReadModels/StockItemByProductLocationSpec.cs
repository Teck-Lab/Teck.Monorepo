using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>Selects the single stock record for a tenant's product at a specific location.</summary>
/// <remarks>
/// Tenant-scoped explicitly as defence in depth. The expiry sweep establishes the owning tenant
/// before this lookup, so Finbuckle's global tenant filter remains active for the read and commit.
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
            .Where(item => item.TenantId == tenantId && item.ProductId == productId && item.LocationId == locationId);
    }
}

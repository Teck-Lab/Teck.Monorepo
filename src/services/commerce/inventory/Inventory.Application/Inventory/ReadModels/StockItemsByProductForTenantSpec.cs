using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>
/// Selects every stock record for a product across all locations for a single tenant. Ordering by
/// location priority (for allocation purposes) is applied by the caller, not by this specification.
/// The explicit tenant predicate keeps allocation deterministic even when this runs outside an
/// ambient tenant-filtering scope (e.g. inside a message consumer).
/// </summary>
public sealed class StockItemsByProductForTenantSpec : Specification<StockItem>
{
    /// <summary>Initializes a new instance of the <see cref="StockItemsByProductForTenantSpec"/> class.</summary>
    /// <param name="productId">The product identifier to match.</param>
    /// <param name="tenantId">The owning tenant identifier to match.</param>
    public StockItemsByProductForTenantSpec(Guid productId, string tenantId) =>
        Query.Where(item => item.ProductId == productId && item.TenantId == tenantId);
}

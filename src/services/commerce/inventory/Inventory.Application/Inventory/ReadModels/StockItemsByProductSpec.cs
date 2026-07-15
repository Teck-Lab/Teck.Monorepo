using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>
/// Selects every stock record for a product across all locations. Ordering by location priority
/// (for allocation purposes) is applied by the caller, not by this specification.
/// </summary>
public sealed class StockItemsByProductSpec : Specification<StockItem>
{
    /// <summary>Initializes a new instance of the <see cref="StockItemsByProductSpec"/> class.</summary>
    /// <param name="productId">The product identifier to match.</param>
    public StockItemsByProductSpec(Guid productId) => Query.Where(item => item.ProductId == productId);
}

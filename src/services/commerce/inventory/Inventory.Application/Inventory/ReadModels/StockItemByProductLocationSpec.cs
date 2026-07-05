using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>Selects the single stock record for a product at a specific location.</summary>
public sealed class StockItemByProductLocationSpec : Specification<StockItem>
{
    /// <summary>Initializes a new instance of the <see cref="StockItemByProductLocationSpec"/> class.</summary>
    /// <param name="productId">The product identifier to match.</param>
    /// <param name="locationId">The location identifier to match.</param>
    public StockItemByProductLocationSpec(Guid productId, Guid locationId) =>
        Query.Where(item => item.ProductId == productId && item.LocationId == locationId);
}

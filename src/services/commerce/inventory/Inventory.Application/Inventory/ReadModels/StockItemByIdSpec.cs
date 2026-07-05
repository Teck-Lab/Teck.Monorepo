using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>Selects a single stock item by its identifier.</summary>
public sealed class StockItemByIdSpec : Specification<StockItem>
{
    /// <summary>Initializes a new instance of the <see cref="StockItemByIdSpec"/> class.</summary>
    /// <param name="stockItemId">The stock item identifier to match.</param>
    public StockItemByIdSpec(Guid stockItemId) => Query.Where(item => item.Id == stockItemId);
}

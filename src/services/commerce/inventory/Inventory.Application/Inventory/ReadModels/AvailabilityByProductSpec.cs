using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>Selects every stock record for a product across all locations, for summing total availability.</summary>
public sealed class AvailabilityByProductSpec : Specification<StockItem>
{
    /// <summary>Initializes a new instance of the <see cref="AvailabilityByProductSpec"/> class.</summary>
    /// <param name="productId">The product identifier to match.</param>
    public AvailabilityByProductSpec(Guid productId) => Query.Where(item => item.ProductId == productId);
}

using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>Selects every stock record for a product across all locations, for summing total availability.</summary>
public sealed class AvailabilityByProductSpec : Specification<StockItem>
{
    /// <summary>Initializes a new instance of the <see cref="AvailabilityByProductSpec"/> class.</summary>
    /// <param name="productId">The product identifier to match.</param>
    /// <param name="locationId">An optional location identifier that, when supplied, restricts the match to that single location.</param>
    public AvailabilityByProductSpec(Guid productId, Guid? locationId = null) =>
        Query.Where(item => item.ProductId == productId && (locationId == null || item.LocationId == locationId));
}

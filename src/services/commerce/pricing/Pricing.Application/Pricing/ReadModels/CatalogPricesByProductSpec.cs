using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects catalog fallback prices for a group of products.</summary>
public sealed class CatalogPricesByProductSpec : Specification<CatalogPrice>
{
    /// <summary>Initializes the product set lookup.</summary>
    /// <param name="productIds">The catalog product identifiers.</param>
    public CatalogPricesByProductSpec(IReadOnlyCollection<Guid> productIds) => Query.Where(price => productIds.Contains(price.ProductId));
}

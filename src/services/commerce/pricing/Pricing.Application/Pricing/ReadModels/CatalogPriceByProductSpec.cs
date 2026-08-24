using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects the catalog fallback price for one product.</summary>
public sealed class CatalogPriceByProductSpec : Specification<CatalogPrice>
{
    /// <summary>Initializes the product lookup.</summary>
    /// <param name="productId">The catalog product identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    public CatalogPriceByProductSpec(Guid productId, string tenantId) =>
        Query.Where(price => price.ProductId == productId && price.TenantId == tenantId);

    /// <summary>Initializes a current-tenant product lookup.</summary>
    /// <param name="productId">The catalog product identifier.</param>
    public CatalogPriceByProductSpec(Guid productId) => Query.Where(price => price.ProductId == productId);
}

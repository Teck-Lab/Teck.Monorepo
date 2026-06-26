using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Suppliers.ReadModels;

/// <summary>Selects the product that owns the given variant (owned tree loaded automatically).</summary>
public sealed class ProductByVariantSpec : Specification<Product>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="variantId">The identifier of the variant whose owning product is selected.</param>
    public ProductByVariantSpec(Guid variantId) => Query.Where(p => p.Variants.Any(v => v.Id == variantId));
}

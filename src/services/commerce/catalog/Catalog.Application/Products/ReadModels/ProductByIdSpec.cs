using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products.ReadModels;

/// <summary>Selects a single product by id (owned variants are loaded automatically).</summary>
public sealed class ProductByIdSpec : Specification<Product>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="productId">The identifier of the product to select.</param>
    public ProductByIdSpec(Guid productId) => Query.Where(p => p.Id == productId);
}

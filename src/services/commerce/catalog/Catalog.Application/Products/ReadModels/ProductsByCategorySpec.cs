using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products.ReadModels;

/// <summary>Lists products, optionally filtered by category, ordered by name.</summary>
public sealed class ProductsByCategorySpec : Specification<Product>
{
    /// <summary>Initializes the spec. A null <paramref name="categoryId"/> returns all products.</summary>
    /// <param name="categoryId">The optional category identifier to filter products by; null returns all products.</param>
    public ProductsByCategorySpec(Guid? categoryId)
    {
        if (categoryId is not null)
        {
            Query.Where(p => p.CategoryId == categoryId);
        }

        Query.OrderBy(p => p.Name);
    }
}

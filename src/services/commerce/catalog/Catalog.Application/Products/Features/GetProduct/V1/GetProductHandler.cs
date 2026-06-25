using Ardalis.Specification;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using ErrorOr;

namespace Catalog.Application.Products.Features.GetProduct.V1;

/// <summary>Handles <see cref="GetProductQuery"/>.</summary>
public static class GetProductHandler
{
    /// <summary>Returns the product DTO or a NotFound error.</summary>
    public static async Task<ErrorOr<ProductDto>> Handle(
        GetProductQuery query,
        IRepositoryBase<Product> repository,
        CancellationToken ct)
    {
        var product = await repository.FirstOrDefaultAsync(new ProductByIdSpec(query.ProductId), ct).ConfigureAwait(false);

        return product is null
            ? Error.NotFound(description: $"Product '{query.ProductId}' was not found.")
            : product.ToDto();
    }
}

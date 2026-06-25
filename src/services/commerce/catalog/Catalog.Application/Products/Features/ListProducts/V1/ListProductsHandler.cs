using Ardalis.Specification;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using ErrorOr;

namespace Catalog.Application.Products.Features.ListProducts.V1;

/// <summary>Handles <see cref="ListProductsQuery"/>.</summary>
public static class ListProductsHandler
{
    /// <summary>Returns product summaries.</summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<ErrorOr<IReadOnlyList<ProductSummaryDto>>> Handle(
        ListProductsQuery query,
        IRepositoryBase<Product> repository,
        CancellationToken ct)
    {
        var products = await repository.ListAsync(new ProductsByCategorySpec(query.CategoryId), ct).ConfigureAwait(false);
        return products.ToSummaries().ToErrorOr();
    }
}

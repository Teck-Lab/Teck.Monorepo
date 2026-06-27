using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Catalog.Application.Products.Features.ListProducts.V1;

/// <summary>Handles <see cref="ListProductsQuery"/>.</summary>
public static class ListProductsHandler
{
    /// <summary>Returns product summaries.</summary>
    /// <param name="query">The query describing the optional category filter.</param>
    /// <param name="repository">The repository used to load the products.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task resolving to the list of product summaries.</returns>
    public static async Task<ErrorOr<IReadOnlyList<ProductSummaryDto>>> Handle(
        ListProductsQuery query,
        IGenericReadRepository<Product, Guid> repository,
        CancellationToken ct)
    {
        var products = await repository.ListAsync(new ProductsByCategorySpec(query.CategoryId), ct).ConfigureAwait(false);
        return products.ToSummaries().ToErrorOr();
    }
}

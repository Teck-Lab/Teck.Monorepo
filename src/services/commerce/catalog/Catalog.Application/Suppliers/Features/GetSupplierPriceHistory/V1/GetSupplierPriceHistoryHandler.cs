using Ardalis.Specification;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using ErrorOr;

namespace Catalog.Application.Suppliers.Features.GetSupplierPriceHistory.V1;

/// <summary>Handles <see cref="GetSupplierPriceHistoryQuery"/>.</summary>
public static class GetSupplierPriceHistoryHandler
{
    /// <summary>Loads the owning product, navigates to the link, and maps its history.</summary>
    /// <param name="query">The query identifying the variant and supplier whose price history is requested.</param>
    /// <param name="repository">The repository used to load the owning product.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<ErrorOr<IReadOnlyList<SupplierPriceHistoryDto>>> Handle(
        GetSupplierPriceHistoryQuery query,
        IRepositoryBase<Product> repository,
        CancellationToken ct)
    {
        var product = await repository.FirstOrDefaultAsync(new ProductByVariantSpec(query.VariantId), ct).ConfigureAwait(false);
        if (product is null)
        {
            return Error.NotFound(description: $"Variant '{query.VariantId}' was not found.");
        }

        var variant = product.Variants.FirstOrDefault(v => v.Id == query.VariantId);
        var link = variant?.Suppliers.FirstOrDefault(s => s.SupplierId == query.SupplierId);
        if (link is null)
        {
            return Error.NotFound(description: $"Supplier '{query.SupplierId}' is not linked to variant '{query.VariantId}'.");
        }

        return link.PriceHistory.ToPriceHistory().ToErrorOr();
    }
}

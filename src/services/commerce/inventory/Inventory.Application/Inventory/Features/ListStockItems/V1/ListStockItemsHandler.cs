using Inventories.Application.Inventory.Mapping;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using SharedKernel.Core.Database;

namespace Inventories.Application.Inventory.Features.ListStockItems.V1;

/// <summary>Handles <see cref="ListStockItemsQuery"/>.</summary>
public static class ListStockItemsHandler
{
    /// <summary>Lists every stock record for a product across all locations.</summary>
    /// <param name="query">The query identifying the product to list stock records for.</param>
    /// <param name="repository">The repository used to query stock items.</param>
    /// <param name="ct">A token used to cancel the operation.</param>
    /// <returns>The stock records for the product.</returns>
    public static async Task<IReadOnlyList<StockItemDto>> Handle(
        ListStockItemsQuery query,
        IGenericReadRepository<StockItem, Guid> repository,
        CancellationToken ct)
    {
        var items = await repository.ListAsync(new StockItemsByProductSpec(query.ProductId), ct).ConfigureAwait(false);
        return items.Select(item => item.ToDto()).ToList();
    }
}

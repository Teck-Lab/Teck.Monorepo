using Inventories.Application.Inventory.Responses;
using SharedKernel.Core.CQRS;

namespace Inventories.Application.Inventory.Features.ListStockItems.V1;

/// <summary>Query that lists every stock record for a product across all locations.</summary>
/// <param name="ProductId">The product identifier to match.</param>
public sealed record ListStockItemsQuery(Guid ProductId) : IQuery<IReadOnlyList<StockItemDto>>;

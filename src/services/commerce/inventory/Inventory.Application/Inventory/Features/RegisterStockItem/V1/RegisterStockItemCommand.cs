using Inventories.Application.Inventory.Responses;
using SharedKernel.Core.CQRS;

namespace Inventories.Application.Inventory.Features.RegisterStockItem.V1;

/// <summary>Command that registers a new stock item for a product at a location.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="LocationId">The location identifier.</param>
/// <param name="QuantityOnHand">The initial quantity on hand.</param>
/// <param name="AllowBackorder">Whether reservations may exceed available stock.</param>
/// <param name="ReorderThreshold">The available-quantity threshold that triggers a reorder.</param>
public sealed record RegisterStockItemCommand(
    Guid ProductId,
    Guid LocationId,
    int QuantityOnHand,
    bool AllowBackorder,
    int ReorderThreshold) : ICommand<StockItemDto>;

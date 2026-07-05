using Inventories.Application.Inventory.Responses;
using SharedKernel.Core.CQRS;

namespace Inventories.Application.Inventory.Features.AdjustStock.V1;

/// <summary>Command that adjusts a stock item's quantity on hand by a signed delta.</summary>
/// <param name="StockItemId">The stock item to adjust.</param>
/// <param name="Delta">The signed adjustment to apply to the quantity on hand.</param>
public sealed record AdjustStockCommand(Guid StockItemId, int Delta) : ICommand<StockItemDto>;

using Inventories.Application.Inventory.Responses;
using SharedKernel.Core.CQRS;

namespace Inventories.Application.Inventory.Features.SetPolicy.V1;

/// <summary>Command that updates a stock item's backorder and reorder-threshold policy.</summary>
/// <param name="StockItemId">The stock item to update.</param>
/// <param name="AllowBackorder">Whether reservations may exceed available stock.</param>
/// <param name="ReorderThreshold">The available-quantity threshold that triggers a reorder.</param>
public sealed record SetPolicyCommand(Guid StockItemId, bool AllowBackorder, int ReorderThreshold) : ICommand<StockItemDto>;

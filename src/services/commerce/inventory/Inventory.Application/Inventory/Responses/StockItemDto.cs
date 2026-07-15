namespace Inventories.Application.Inventory.Responses;

/// <summary>Represents a stock item in API responses.</summary>
/// <param name="Id">The stock item identifier.</param>
/// <param name="ProductId">The identifier of the product this stock record tracks.</param>
/// <param name="LocationId">The identifier of the location this stock record tracks.</param>
/// <param name="OnHand">The quantity currently on hand at the location.</param>
/// <param name="Reserved">The quantity currently reserved against on-hand stock.</param>
/// <param name="Available">The quantity available to promise (on hand minus reserved).</param>
/// <param name="AllowBackorder">Whether reservations may exceed available stock.</param>
/// <param name="ReorderThreshold">The available-quantity threshold that triggers a reorder.</param>
public sealed record StockItemDto(
    Guid Id,
    Guid ProductId,
    Guid LocationId,
    int OnHand,
    int Reserved,
    int Available,
    bool AllowBackorder,
    int ReorderThreshold);

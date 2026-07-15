namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Request to adjust a stock item's quantity on hand.</summary>
/// <param name="Id">The stock item identifier (bound from route).</param>
/// <param name="Delta">The signed adjustment to apply to the quantity on hand.</param>
public sealed record AdjustStockRequest(Guid Id, int Delta);

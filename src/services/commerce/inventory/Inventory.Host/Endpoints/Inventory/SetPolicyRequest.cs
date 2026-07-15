namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Request to update a stock item's backorder and reorder-threshold policy.</summary>
/// <param name="Id">The stock item identifier (bound from route).</param>
/// <param name="AllowBackorder">Whether reservations may exceed available stock.</param>
/// <param name="ReorderThreshold">The available-quantity threshold that triggers a reorder.</param>
public sealed record SetPolicyRequest(Guid Id, bool AllowBackorder, int ReorderThreshold);

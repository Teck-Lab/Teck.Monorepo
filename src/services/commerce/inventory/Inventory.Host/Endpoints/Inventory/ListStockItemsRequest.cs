namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Request to list stock records for a product across all locations.</summary>
/// <param name="ProductId">The product identifier (bound from query string).</param>
public sealed record ListStockItemsRequest(Guid ProductId);

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to add an item to a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier.</param>
/// <param name="ProductName">The product name.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="Quantity">The quantity to add.</param>
public sealed record AddItemRequest(Guid BasketId, Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

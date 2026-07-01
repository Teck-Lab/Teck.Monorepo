namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to remove an item from a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier to remove (bound from route).</param>
public sealed record RemoveItemRequest(Guid BasketId, Guid ProductId);

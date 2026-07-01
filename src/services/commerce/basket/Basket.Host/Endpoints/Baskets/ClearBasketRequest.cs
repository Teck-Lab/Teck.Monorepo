namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to clear all items from a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
public sealed record ClearBasketRequest(Guid BasketId);

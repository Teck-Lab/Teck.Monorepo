namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to check out a basket.</summary>
/// <param name="BasketId">The basket to check out.</param>
public sealed record CheckoutBasketRequest(Guid BasketId);

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to update a basket line quantity.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier (bound from route).</param>
/// <param name="Quantity">The new quantity.</param>
public sealed record UpdateItemRequest(Guid BasketId, Guid ProductId, int Quantity);

namespace Baskets.Application.Baskets.Responses;

/// <summary>Represents a single basket line in API responses.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="ProductName">The product name.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="LineTotal">The line total.</param>
public sealed record BasketItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

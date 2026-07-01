namespace Baskets.Domain.ValueObjects;

/// <summary>
/// Represents a single line within a basket as an immutable value object.
/// </summary>
/// <param name="ProductId">The identifier of the product.</param>
/// <param name="ProductName">The name of the product captured at add-time.</param>
/// <param name="UnitPrice">The price per unit captured at add-time.</param>
/// <param name="Quantity">The quantity of the product in the basket.</param>
public sealed record BasketItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    /// <summary>Gets the total monetary amount for this line.</summary>
    public decimal LineTotal => UnitPrice * Quantity;
}

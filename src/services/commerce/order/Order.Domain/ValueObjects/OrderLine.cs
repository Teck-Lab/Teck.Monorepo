namespace Orders.Domain.ValueObjects;

/// <summary>
/// Represents a single line within an order as an immutable value object.
/// </summary>
/// <param name="ProductId">The identifier of the ordered product.</param>
/// <param name="ProductName">The name of the ordered product.</param>
/// <param name="Quantity">The quantity of the product ordered.</param>
/// <param name="UnitPrice">The price per unit of the product.</param>
public sealed record OrderLine(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice)
{
    /// <summary>
    /// Gets the total monetary amount for this line.
    /// </summary>
    public decimal Total => Quantity * UnitPrice;
}

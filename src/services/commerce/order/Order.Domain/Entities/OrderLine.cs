namespace Orders.Domain.Entities;

/// <summary>
/// Represents a single line within an order.
/// </summary>
public sealed class OrderLine
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderLine"/> class.
    /// </summary>
    /// <param name="productId">The identifier of the ordered product.</param>
    /// <param name="productName">The name of the ordered product.</param>
    /// <param name="quantity">The quantity of the product ordered.</param>
    /// <param name="unitPrice">The price per unit of the product.</param>
    public OrderLine(
        Guid productId,
        string productName,
        int quantity,
        decimal unitPrice)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>
    /// Gets the identifier of the ordered product.
    /// </summary>
    public Guid ProductId { get; }

    /// <summary>
    /// Gets the name of the ordered product.
    /// </summary>
    public string ProductName { get; }

    /// <summary>
    /// Gets the quantity of the product ordered.
    /// </summary>
    public int Quantity { get; }

    /// <summary>
    /// Gets the price per unit of the product.
    /// </summary>
    public decimal UnitPrice { get; }

    /// <summary>
    /// Gets the total monetary amount for this line.
    /// </summary>
    public decimal Total => Quantity * UnitPrice;
}

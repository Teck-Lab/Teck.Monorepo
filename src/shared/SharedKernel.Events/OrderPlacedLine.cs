using MemoryPack;

namespace SharedKernel.Events;

/// <summary>A single line carried by <see cref="OrderPlacedIntegrationEvent"/>.</summary>
[MemoryPackable]
public partial class OrderPlacedLine
{
    /// <summary>Initializes a new instance of the <see cref="OrderPlacedLine"/> class.</summary>
    public OrderPlacedLine()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OrderPlacedLine"/> class.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="productName">The product name.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="total">The line total.</param>
    [MemoryPackConstructor]
    public OrderPlacedLine(Guid productId, string productName, int quantity, decimal unitPrice, decimal total)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Total = total;
    }

    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the product name.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Gets or sets the quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the unit price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets or sets the line total.</summary>
    public decimal Total { get; set; }
}

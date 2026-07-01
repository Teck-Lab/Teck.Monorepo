using MemoryPack;

namespace SharedKernel.Events;

/// <summary>A single line carried by <see cref="BasketCheckedOutIntegrationEvent"/>.</summary>
[MemoryPackable]
public partial class BasketCheckedOutLine
{
    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOutLine"/> class.</summary>
    public BasketCheckedOutLine()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOutLine"/> class.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="productName">The product name.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="lineTotal">The line total.</param>
    [MemoryPackConstructor]
    public BasketCheckedOutLine(Guid productId, string productName, decimal unitPrice, int quantity, decimal lineTotal)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = lineTotal;
    }

    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the product name.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Gets or sets the unit price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the line total.</summary>
    public decimal LineTotal { get; set; }
}

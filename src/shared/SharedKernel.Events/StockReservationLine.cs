using MemoryPack;

namespace SharedKernel.Events;

/// <summary>A single line carried by <see cref="StockReservedIntegrationEvent"/> and <see cref="StockReservationRejectedIntegrationEvent"/>.</summary>
[MemoryPackable]
public partial class StockReservationLine
{
    /// <summary>Initializes a new instance of the <see cref="StockReservationLine"/> class.</summary>
    public StockReservationLine()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StockReservationLine"/> class.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="requestedQuantity">The requested quantity.</param>
    /// <param name="backorderedQuantity">The backordered quantity.</param>
    [MemoryPackConstructor]
    public StockReservationLine(Guid productId, int requestedQuantity, int backorderedQuantity)
    {
        ProductId = productId;
        RequestedQuantity = requestedQuantity;
        BackorderedQuantity = backorderedQuantity;
    }

    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the requested quantity.</summary>
    public int RequestedQuantity { get; set; }

    /// <summary>Gets or sets the backordered quantity.</summary>
    public int BackorderedQuantity { get; set; }
}

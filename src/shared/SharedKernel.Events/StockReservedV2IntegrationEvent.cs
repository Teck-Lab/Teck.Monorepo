using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes a version-two successful stock reservation outcome.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class StockReservedV2IntegrationEvent
{
    /// <summary>Gets or sets the reservation identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid ReservationId { get; set; }
    /// <summary>Gets or sets the order identifier when the source is an order.</summary>
    [MemoryPackOrder(1)]
    public Guid? OrderId { get; set; }
    /// <summary>Gets or sets the basket identifier when applicable.</summary>
    [MemoryPackOrder(2)]
    public Guid? BasketId { get; set; }
    /// <summary>Gets or sets the source type.</summary>
    [MemoryPackOrder(3)]
    public string SourceType { get; set; } = string.Empty;
    /// <summary>Gets or sets the source identifier.</summary>
    [MemoryPackOrder(4)]
    public Guid SourceId { get; set; }
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(5)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(6)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable idempotency key.</summary>
    [MemoryPackOrder(7)]
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>Gets or sets the reserved lines.</summary>
    [MemoryPackOrder(8)]
    public List<StockReservationLine> Lines { get; set; } = [];
}

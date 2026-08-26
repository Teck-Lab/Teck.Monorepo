using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Requests an authoritative price and ceiling check before a backorder proceeds.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BackorderPriceCheckRequestedIntegrationEvent
{
    /// <summary>Gets or sets the order identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid OrderId { get; set; }
    /// <summary>Gets or sets the basket identifier when applicable.</summary>
    [MemoryPackOrder(1)]
    public Guid? BasketId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(2)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the shopper-authorized maximum total.</summary>
    [MemoryPackOrder(3)]
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(4)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(5)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable price-check request key.</summary>
    [MemoryPackOrder(6)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the order lines requiring a price check.</summary>
    [MemoryPackOrder(7)]
    public List<OrderPlacedLine> Lines { get; set; } = [];
}

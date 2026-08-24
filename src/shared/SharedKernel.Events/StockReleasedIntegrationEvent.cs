using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes completion of an idempotent stock release.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class StockReleasedIntegrationEvent
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
    /// <summary>Gets or sets the originating source correlation identifier.</summary>
    [MemoryPackOrder(3)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable release request key.</summary>
    [MemoryPackOrder(4)]
    public string RequestId { get; set; } = string.Empty;
}

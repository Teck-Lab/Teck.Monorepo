using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes expiry of an unfilled order backorder.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BackorderExpiredIntegrationEvent
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
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(3)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable outcome key.</summary>
    [MemoryPackOrder(4)]
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>Gets or sets when the backorder expired.</summary>
    [MemoryPackOrder(5)]
    public DateTimeOffset ExpiredAt { get; set; }
}

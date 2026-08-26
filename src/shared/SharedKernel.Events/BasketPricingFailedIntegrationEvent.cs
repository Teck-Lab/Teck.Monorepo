using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes a structured, non-sensitive failure to price a basket.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BasketPricingFailedIntegrationEvent
{
    /// <summary>Gets or sets the basket identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid BasketId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(1)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable pricing request key.</summary>
    [MemoryPackOrder(2)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the checkout correlation identifier.</summary>
    [MemoryPackOrder(3)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the safe failure category.</summary>
    [MemoryPackOrder(4)]
    public string FailureCategory { get; set; } = string.Empty;
    /// <summary>Gets or sets shopper-safe action text.</summary>
    [MemoryPackOrder(5)]
    public string ActionText { get; set; } = string.Empty;
}

using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes an authoritative basket pricing result.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BasketPricedIntegrationEvent
{
    /// <summary>Gets or sets the basket identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid BasketId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(1)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the resolved basket total.</summary>
    [MemoryPackOrder(2)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the shopper-authorized maximum total.</summary>
    [MemoryPackOrder(3)]
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(4)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable pricing request key.</summary>
    [MemoryPackOrder(5)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the checkout correlation identifier.</summary>
    [MemoryPackOrder(6)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the priced lines.</summary>
    [MemoryPackOrder(7)]
    public List<BasketPricedLine> Lines { get; set; } = [];
}

using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Requests authoritative pricing for a checked-out basket.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BasketCheckoutRequestedIntegrationEvent
{
    /// <summary>Gets or sets the basket identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid BasketId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(1)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the shopper-authorized maximum total.</summary>
    [MemoryPackOrder(2)]
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(3)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable pricing request key.</summary>
    [MemoryPackOrder(4)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the checkout correlation identifier.</summary>
    [MemoryPackOrder(5)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the basket lines to price.</summary>
    [MemoryPackOrder(6)]
    public List<BasketCheckoutRequestedLine> Lines { get; set; } = [];
}

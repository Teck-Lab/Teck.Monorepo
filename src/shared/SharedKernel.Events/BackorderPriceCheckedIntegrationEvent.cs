using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes the structured outcome of a backorder price and ceiling check.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BackorderPriceCheckedIntegrationEvent
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
    /// <summary>Gets or sets the resolved amount.</summary>
    [MemoryPackOrder(3)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the shopper-authorized maximum total.</summary>
    [MemoryPackOrder(4)]
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(5)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets whether the resolved amount is within the ceiling.</summary>
    [MemoryPackOrder(6)]
    public bool IsWithinAuthorizedAmount { get; set; }
    /// <summary>Gets or sets the safe failure category when the check fails.</summary>
    [MemoryPackOrder(7)]
    public string FailureCategory { get; set; } = string.Empty;
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(8)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable request key being answered.</summary>
    [MemoryPackOrder(9)]
    public string RequestId { get; set; } = string.Empty;
}

using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Returns the current catalog sell price for a reconciliation request.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class CatalogPriceReconciledIntegrationEvent
{
    /// <summary>Gets or sets the product identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid ProductId { get; set; }
    /// <summary>Gets or sets the default variant identifier.</summary>
    [MemoryPackOrder(1)]
    public Guid VariantId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(2)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the current catalog sell price.</summary>
    [MemoryPackOrder(3)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(4)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable request key being answered.</summary>
    [MemoryPackOrder(5)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(6)]
    public string SourceCorrelationId { get; set; } = string.Empty;
}

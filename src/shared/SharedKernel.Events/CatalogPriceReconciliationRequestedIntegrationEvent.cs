using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Requests a tenant-safe catalog price reconciliation without a synchronous dependency.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class CatalogPriceReconciliationRequestedIntegrationEvent
{
    /// <summary>Gets or sets the product identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid ProductId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(1)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable reconciliation request key.</summary>
    [MemoryPackOrder(2)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(3)]
    public string SourceCorrelationId { get; set; } = string.Empty;
}

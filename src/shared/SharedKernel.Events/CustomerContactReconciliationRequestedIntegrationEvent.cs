using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Requests tenant-safe asynchronous reconciliation of a shopper contact.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class CustomerContactReconciliationRequestedIntegrationEvent
{
    /// <summary>Gets or sets the customer identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid CustomerId { get; set; }
    /// <summary>Gets or sets the immutable shopper subject.</summary>
    [MemoryPackOrder(1)]
    public string KeycloakSubjectId { get; set; } = string.Empty;
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(2)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable reconciliation request key.</summary>
    [MemoryPackOrder(3)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(4)]
    public string SourceCorrelationId { get; set; } = string.Empty;
}

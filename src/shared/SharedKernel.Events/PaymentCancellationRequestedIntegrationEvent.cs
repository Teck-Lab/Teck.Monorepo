using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Requests idempotent cancellation of payment processing for an order.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class PaymentCancellationRequestedIntegrationEvent
{
    /// <summary>Gets or sets the order identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid OrderId { get; set; }
    /// <summary>Gets or sets the payment identifier when known.</summary>
    [MemoryPackOrder(1)]
    public Guid? PaymentId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(2)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable cancellation request key.</summary>
    [MemoryPackOrder(3)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(4)]
    public string SourceCorrelationId { get; set; } = string.Empty;
}

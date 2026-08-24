using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Requests an idempotent retry using a replacement opaque payment token.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class PaymentRetryRequestedIntegrationEvent
{
    /// <summary>Gets or sets the order identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid OrderId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(1)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the immutable shopper-authorized maximum total.</summary>
    [MemoryPackOrder(2)]
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(3)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the bounded opaque replacement payment-method token.</summary>
    [MemoryPackOrder(4)]
    public string PaymentMethodToken { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable retry request key.</summary>
    [MemoryPackOrder(5)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(6)]
    public string SourceCorrelationId { get; set; } = string.Empty;
}

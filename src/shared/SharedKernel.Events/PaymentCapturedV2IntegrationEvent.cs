using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes a safe, version-two successful payment outcome.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class PaymentCapturedV2IntegrationEvent
{
    /// <summary>Gets or sets the payment identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid PaymentId { get; set; }
    /// <summary>Gets or sets the order identifier.</summary>
    [MemoryPackOrder(1)]
    public Guid OrderId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(2)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the captured amount.</summary>
    [MemoryPackOrder(3)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the immutable authorized ceiling.</summary>
    [MemoryPackOrder(4)]
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(5)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable payment request identifier.</summary>
    [MemoryPackOrder(6)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the source correlation identifier.</summary>
    [MemoryPackOrder(7)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets when capture completed.</summary>
    [MemoryPackOrder(8)]
    public DateTimeOffset CapturedAt { get; set; }
}

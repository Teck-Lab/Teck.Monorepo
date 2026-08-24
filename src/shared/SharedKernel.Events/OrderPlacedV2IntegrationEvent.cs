using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes a version-two order placement for payment and stock processing.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class OrderPlacedV2IntegrationEvent
{
    /// <summary>Gets or sets the order identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid OrderId { get; set; }
    /// <summary>Gets or sets the originating basket identifier.</summary>
    [MemoryPackOrder(1)]
    public Guid BasketId { get; set; }
    /// <summary>Gets or sets the optional customer identifier.</summary>
    [MemoryPackOrder(2)]
    public Guid? CustomerId { get; set; }
    /// <summary>Gets or sets the immutable shopper subject.</summary>
    [MemoryPackOrder(3)]
    public string KeycloakSubjectId { get; set; } = string.Empty;
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(4)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the platform-resolved order amount.</summary>
    [MemoryPackOrder(5)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the immutable authorized ceiling.</summary>
    [MemoryPackOrder(6)]
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(7)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the bounded opaque payment-method token.</summary>
    [MemoryPackOrder(8)]
    public string PaymentMethodToken { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable provider request identifier.</summary>
    [MemoryPackOrder(9)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Gets or sets the originating checkout correlation identifier.</summary>
    [MemoryPackOrder(10)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets when the order was created.</summary>
    [MemoryPackOrder(11)]
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the platform-priced order lines.</summary>
    [MemoryPackOrder(12)]
    public List<OrderPlacedLine> Lines { get; set; } = [];
}

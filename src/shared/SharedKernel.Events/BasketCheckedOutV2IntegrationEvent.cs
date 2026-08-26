using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes a version-two, platform-priced checkout for order creation.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BasketCheckedOutV2IntegrationEvent
{
    /// <summary>Gets or sets the basket identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid BasketId { get; set; }
    /// <summary>Gets or sets the optional customer identifier.</summary>
    [MemoryPackOrder(1)]
    public Guid? CustomerId { get; set; }
    /// <summary>Gets or sets the immutable shopper subject.</summary>
    [MemoryPackOrder(2)]
    public string KeycloakSubjectId { get; set; } = string.Empty;
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(3)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the resolved checkout total.</summary>
    [MemoryPackOrder(4)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the shopper-authorized maximum total.</summary>
    [MemoryPackOrder(5)]
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(6)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the bounded opaque payment-method token.</summary>
    [MemoryPackOrder(7)]
    public string PaymentMethodToken { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable checkout correlation identifier.</summary>
    [MemoryPackOrder(8)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets when checkout completed.</summary>
    [MemoryPackOrder(9)]
    public DateTimeOffset CheckedOutAt { get; set; }
    /// <summary>Gets or sets the platform-priced checkout lines.</summary>
    [MemoryPackOrder(10)]
    public List<BasketCheckedOutLineV2> Items { get; set; } = [];
}

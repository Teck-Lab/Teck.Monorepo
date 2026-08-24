using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes a version-two effective price change with a stable idempotency key.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class PriceChangedV2IntegrationEvent
{
    /// <summary>Gets or sets the product identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid ProductId { get; set; }
    /// <summary>Gets or sets the price-list identifier.</summary>
    [MemoryPackOrder(1)]
    public Guid PriceListId { get; set; }
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(2)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the effective amount.</summary>
    [MemoryPackOrder(3)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(4)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets when the price takes effect.</summary>
    [MemoryPackOrder(5)]
    public DateTimeOffset EffectiveFrom { get; set; }
    /// <summary>Gets or sets the change type.</summary>
    [MemoryPackOrder(6)]
    public string ChangeType { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable idempotency key.</summary>
    [MemoryPackOrder(7)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

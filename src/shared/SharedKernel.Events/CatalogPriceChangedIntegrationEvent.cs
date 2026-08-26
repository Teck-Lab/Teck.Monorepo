using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes a catalog sell-price change for asynchronous pricing projection.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class CatalogPriceChangedIntegrationEvent
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
    /// <summary>Gets or sets the catalog sell price.</summary>
    [MemoryPackOrder(3)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(4)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable projection idempotency key.</summary>
    [MemoryPackOrder(5)]
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>Gets or sets when the price changed.</summary>
    [MemoryPackOrder(6)]
    public DateTimeOffset ChangedAt { get; set; }
}

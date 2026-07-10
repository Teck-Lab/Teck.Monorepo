using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when an effective product price changes. Owned by the pricing
/// service. Consumers (basket reprice, search, catalog display) subscribe without referencing pricing.
/// </summary>
[MemoryPackable]
public partial class PriceChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="PriceChangedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public PriceChangedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the product whose price changed.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the owning price list.</summary>
    public Guid PriceListId { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the amount involved in the change.</summary>
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the ISO currency of the amount.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Gets or sets when the change takes effect.</summary>
    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>Gets or sets the change type ("Upserted" or "Removed").</summary>
    public string ChangeType { get; set; } = string.Empty;
}

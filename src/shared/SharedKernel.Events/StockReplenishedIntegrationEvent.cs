using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when a stock item's available quantity at a location has been
/// replenished.
/// </summary>
[MemoryPackable]
public partial class StockReplenishedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="StockReplenishedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public StockReplenishedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the location identifier.</summary>
    public Guid LocationId { get; set; }

    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the available quantity after replenishment.</summary>
    public int Available { get; set; }
}

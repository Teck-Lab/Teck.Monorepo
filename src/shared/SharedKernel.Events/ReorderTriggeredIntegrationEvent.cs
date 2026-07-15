using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when a stock item's available quantity at a location has fallen
/// to or below its reorder threshold.
/// </summary>
[MemoryPackable]
public partial class ReorderTriggeredIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="ReorderTriggeredIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public ReorderTriggeredIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the location identifier.</summary>
    public Guid LocationId { get; set; }

    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the available quantity that triggered the reorder.</summary>
    public int Available { get; set; }

    /// <summary>Gets or sets the reorder threshold that was crossed.</summary>
    public int ReorderThreshold { get; set; }
}

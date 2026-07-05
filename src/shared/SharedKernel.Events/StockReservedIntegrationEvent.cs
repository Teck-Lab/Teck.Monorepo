using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when stock has been successfully reserved for a source
/// (e.g. a checked-out basket or a placed order).
/// </summary>
[MemoryPackable]
public partial class StockReservedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="StockReservedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public StockReservedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the reservation identifier.</summary>
    public Guid ReservationId { get; set; }

    /// <summary>Gets or sets the type of the source that requested the reservation.</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Gets or sets the identifier of the source that requested the reservation.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the reserved lines.</summary>
    public IReadOnlyList<StockReservationLine> Lines { get; set; } = [];
}

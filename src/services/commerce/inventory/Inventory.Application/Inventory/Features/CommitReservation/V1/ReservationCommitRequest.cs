using Inventories.Domain.ValueObjects;

namespace Inventories.Application.Inventory.Features.CommitReservation.V1;

/// <summary>Describes a reservation the <see cref="ReservationCommitter"/> should attempt to commit.</summary>
/// <param name="Source">The kind of aggregate that originated the reservation.</param>
/// <param name="SourceId">The identifier of the originating source aggregate.</param>
/// <param name="TenantId">The owning tenant identifier.</param>
/// <param name="Lines">The requested product lines.</param>
/// <param name="BasketId">The correlated basket for an order lifecycle reservation.</param>
/// <param name="SourceCorrelationId">The stable lifecycle correlation identifier.</param>
internal sealed record ReservationCommitRequest(
    ReservationSource Source,
    Guid SourceId,
    string TenantId,
    IReadOnlyList<ReservationRequestLine> Lines,
    Guid? BasketId = null,
    string SourceCorrelationId = "");

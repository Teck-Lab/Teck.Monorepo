using Ardalis.Specification;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>
/// Selects the reservation originated by a specific source aggregate, keyed by its
/// <see cref="ReservationSource"/> and source identifier. Used for idempotency: a consumer looks
/// this up first and no-ops when a matching reservation already exists.
/// </summary>
public sealed class ReservationBySourceSpec : Specification<Reservation>
{
    /// <summary>Initializes a new instance of the <see cref="ReservationBySourceSpec"/> class.</summary>
    /// <param name="tenantId">The tenant that owns the reservation.</param>
    /// <param name="source">The kind of aggregate that originated the reservation.</param>
    /// <param name="sourceId">The identifier of the originating source aggregate.</param>
    public ReservationBySourceSpec(string tenantId, ReservationSource source, Guid sourceId) =>
        Query.Where(reservation => reservation.TenantId == tenantId && reservation.SourceType == source && reservation.SourceId == sourceId);
}

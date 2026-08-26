using Ardalis.Specification;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>
/// Selects lapsed basket holds and due order backorders for one tenant as of <c>asOf</c>.
/// </summary>
public sealed class ExpiredHeldReservationsSpec : Specification<Reservation>
{
    /// <summary>Initializes a new instance of the <see cref="ExpiredHeldReservationsSpec"/> class.</summary>
    /// <param name="tenantId">The tenant that owns the candidate reservations.</param>
    /// <param name="asOf">The point in time used to decide whether a held reservation has expired.</param>
    public ExpiredHeldReservationsSpec(string tenantId, DateTimeOffset asOf)
    {
        Query
            .Where(reservation =>
                reservation.TenantId == tenantId &&
                ((reservation.Status == ReservationStatus.Held && reservation.ExpiresAt <= asOf) ||
                 (reservation.SourceType == ReservationSource.Order &&
                  reservation.Status == ReservationStatus.Committed &&
                  reservation.BackorderExpiresAt <= asOf &&
                  reservation.Lines.Any(line => line.BackorderedQuantity > 0))));
    }
}

using Ardalis.Specification;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>
/// Selects the reservations that currently count toward a product's reserved stock: committed
/// reservations (always active) and held reservations whose hold has not yet lapsed as of
/// <c>asOf</c>. A held reservation whose <see cref="Reservation.ExpiresAt"/> is at or before
/// <c>asOf</c> is excluded here, so its allocations stop counting toward reserved stock the
/// instant the hold lapses — before the expiry sweep (Task 18) ever persists the release.
/// </summary>
public sealed class ActiveReservationsByProductSpec : Specification<Reservation>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveReservationsByProductSpec"/> class.</summary>
    /// <param name="productId">The product identifier to match (against the reservation's lines).</param>
    /// <param name="asOf">The point in time used to decide whether a held reservation has expired.</param>
    public ActiveReservationsByProductSpec(Guid productId, DateTimeOffset asOf) =>
        Query.Where(reservation =>
            reservation.Lines.Any(line => line.ProductId == productId) &&
            (reservation.Status == ReservationStatus.Committed ||
             (reservation.Status == ReservationStatus.Held && reservation.ExpiresAt > asOf)));
}

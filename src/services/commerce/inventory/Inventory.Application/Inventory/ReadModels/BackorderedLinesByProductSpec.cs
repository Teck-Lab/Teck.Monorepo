using Ardalis.Specification;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>
/// Selects the active reservations (mirroring <see cref="ActiveReservationsByProductSpec"/>'s
/// Committed-or-unexpired-Held rule) that carry an outstanding backordered line for a product,
/// ordered by <c>Reservation.CreatedAt</c> so callers can fill them FIFO — oldest
/// backorder first — as replenished stock becomes available.
/// </summary>
/// <remarks>
/// <see cref="ReservationLine.ProductId"/>, <see cref="ReservationLine.BackorderedQuantity"/>,
/// <see cref="Reservation.Status"/>, and <see cref="Reservation.ExpiresAt"/> are all plain
/// scalar/converted columns on the owned <c>ReservationLines</c>/<c>Reservations</c> tables (see
/// <c>ReservationConfiguration</c>), so this entire predicate — including the
/// <c>Lines.Any(...)</c> owned-collection filter — is SQL-translatable, the same reasoning
/// documented on <see cref="ActiveReservationsByProductSpec"/> (Task 17). Only
/// <see cref="ReservationLine.Allocations"/> is a JSON-converted column; this spec never
/// filters or projects it, so no in-memory step is needed here. Callers that need to
/// read/mutate a line's allocations (e.g. the backorder-fill handler) do so in memory after
/// this already-filtered, already-tenant-scoped result set is loaded.
/// </remarks>
public sealed class BackorderedLinesByProductSpec : Specification<Reservation>
{
    /// <summary>Initializes a new instance of the <see cref="BackorderedLinesByProductSpec"/> class.</summary>
    /// <param name="tenantId">The tenant whose reservations can be filled.</param>
    /// <param name="productId">The product identifier to match (against the reservation's lines).</param>
    /// <param name="asOf">The point in time used to decide whether a held reservation has expired.</param>
    public BackorderedLinesByProductSpec(string tenantId, Guid productId, DateTimeOffset asOf) =>
        Query
            .Where(reservation =>
                reservation.TenantId == tenantId &&
                reservation.Lines.Any(line => line.ProductId == productId && line.BackorderedQuantity > 0) &&
                (reservation.Status == ReservationStatus.Committed &&
                 (reservation.BackorderExpiresAt == null || reservation.BackorderExpiresAt > asOf) ||
                 (reservation.Status == ReservationStatus.Held && reservation.ExpiresAt > asOf)))
            .OrderBy(reservation => reservation.CreatedAt);
}

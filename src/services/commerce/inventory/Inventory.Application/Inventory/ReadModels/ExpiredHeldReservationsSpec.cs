using Ardalis.Specification;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>
/// Selects <see cref="ReservationStatus.Held"/> reservations whose hold has lapsed as of
/// <c>asOf</c> — the candidates the expiry sweep (Task 18) transitions to
/// <see cref="ReservationStatus.Expired"/> and releases.
/// </summary>
/// <remarks>
/// The sweep runs on a background timer, outside any HTTP request / ambient tenant context, and is
/// intentionally cross-tenant: one run expires lapsed holds for every tenant. No aggregate in this
/// platform is currently registered with Finbuckle's <c>[MultiTenant]</c> attribute (see
/// <c>MultiTenantDbContextExtensions.EnforceMultiTenant</c> / <c>ConfigureMultiTenant</c>), so there
/// is presently no EF global query filter here to defeat. <c>IgnoreQueryFilters()</c> is applied
/// anyway, defensively, so this spec keeps behaving correctly — reading across all tenants — if/when
/// tenant-scoped global filtering is wired up for these aggregates.
/// </remarks>
public sealed class ExpiredHeldReservationsSpec : Specification<Reservation>
{
    /// <summary>Initializes a new instance of the <see cref="ExpiredHeldReservationsSpec"/> class.</summary>
    /// <param name="asOf">The point in time used to decide whether a held reservation has expired.</param>
    public ExpiredHeldReservationsSpec(DateTimeOffset asOf)
    {
        Query
            .Where(reservation => reservation.Status == ReservationStatus.Held && reservation.ExpiresAt <= asOf)
            .IgnoreQueryFilters();
    }
}

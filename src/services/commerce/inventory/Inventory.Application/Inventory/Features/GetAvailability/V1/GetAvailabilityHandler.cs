using Inventories.Application.Inventory.ReadModels;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using SharedKernel.Core.Database;

namespace Inventories.Application.Inventory.Features.GetAvailability.V1;

/// <summary>
/// Handles the <see cref="GetAvailabilityQuery"/> by computing EFFECTIVE availability — on-hand
/// quantity minus live (non-expired) reservation allocations — rather than trusting the stored
/// <see cref="StockItem.QuantityReserved"/> counter.
/// </summary>
public static class GetAvailabilityHandler
{
    /// <summary>
    /// Retrieves the total and per-location EFFECTIVE availability for a product, optionally
    /// filtered to a single location.
    /// </summary>
    /// <remarks>
    /// Decision (lazy expiry, Task 17): <see cref="StockItem.QuantityReserved"/> is a stored
    /// counter that still includes <c>Held</c> allocations after their hold has lapsed — it is only
    /// made truthful again once the expiry sweep (Task 18) runs. Rather than read that counter,
    /// this handler computes availability live as
    /// <c>OnHand − (Committed allocations + Held allocations where ExpiresAt &gt; now)</c> via
    /// <see cref="ActiveReservationsByProductSpec"/>, so an expired hold stops counting toward
    /// reserved stock immediately, before any sweep runs.
    /// <para>
    /// A reservation line's per-location allocations are stored as a JSON-converted column (see
    /// <c>ReservationConfiguration</c>) that EF Core cannot translate a <c>SelectMany</c>/<c>GroupBy</c>
    /// over into SQL. The spec pushes the (SQL-translatable) status/expiry filtering down to the
    /// database, and only the per-location aggregation over the JSON allocations column is done in
    /// memory, over the already-filtered, already-tenant-scoped result set.
    /// </para>
    /// </remarks>
    /// <param name="query">The query identifying the product (and optional location) to check.</param>
    /// <param name="stockItems">The repository used to query stock items.</param>
    /// <param name="reservations">The repository used to query active reservations.</param>
    /// <param name="timeProvider">The clock used to decide whether a held reservation has expired.</param>
    /// <param name="ct">A token used to cancel the operation.</param>
    /// <returns>The aggregate and per-location EFFECTIVE availability for the product.</returns>
    public static async Task<AvailabilityDto> Handle(
        GetAvailabilityQuery query,
        IGenericReadRepository<StockItem, Guid> stockItems,
        IGenericReadRepository<Reservation, Guid> reservations,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var stockSpec = new AvailabilityByProductSpec(query.ProductId, query.LocationId);
        IReadOnlyList<StockItem> items = await stockItems.ListAsync(stockSpec, ct).ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();
        var reservationSpec = new ActiveReservationsByProductSpec(query.ProductId, now);
        IReadOnlyList<Reservation> active = await reservations.ListAsync(reservationSpec, ct).ConfigureAwait(false);

        Dictionary<Guid, int> effectiveReservedByLocation = active
            .SelectMany(reservation => reservation.Lines)
            .Where(line => line.ProductId == query.ProductId)
            .SelectMany(line => line.Allocations)
            .GroupBy(allocation => allocation.LocationId)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Quantity));

        var byLocation = items
            .Select(item => new LocationAvailabilityDto(
                item.LocationId,
                item.QuantityOnHand - effectiveReservedByLocation.GetValueOrDefault(item.LocationId)))
            .ToList();

        return new AvailabilityDto(query.ProductId, byLocation.Sum(location => location.Available), byLocation);
    }
}

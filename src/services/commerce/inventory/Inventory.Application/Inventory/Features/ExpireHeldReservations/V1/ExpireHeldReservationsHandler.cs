using Inventories.Application.Inventory.ReadModels;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using SharedKernel.Core.Database;

namespace Inventories.Application.Inventory.Features.ExpireHeldReservations.V1;

/// <summary>Handles <see cref="ExpireHeldReservationsCommand"/>.</summary>
public static class ExpireHeldReservationsHandler
{
    /// <summary>
    /// Loads every <see cref="ReservationStatus.Held"/> reservation whose hold has lapsed
    /// (across all tenants — see <see cref="ExpiredHeldReservationsSpec"/>), transitions each to
    /// <see cref="ReservationStatus.Expired"/>, and releases its allocations against the
    /// corresponding <see cref="StockItem"/> records, making the stored
    /// <see cref="StockItem.QuantityReserved"/> counter truthful again. This is the housekeeping
    /// that bounds the write-side conservatism of the lazy-expiry read path (Task 17): reads
    /// already ignore expired holds immediately, but the stored counter used by the write/allocation
    /// path is only corrected once this sweep runs.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="reservations">The reservation write repository.</param>
    /// <param name="stockItems">The stock write repository.</param>
    /// <param name="unitOfWork">The unit of work (single commit point for the whole sweep).</param>
    /// <param name="timeProvider">The clock used to decide whether a held reservation has expired.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of reservations expired by this sweep.</returns>
    public static async Task<int> Handle(
        ExpireHeldReservationsCommand command,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        DateTimeOffset now = timeProvider.GetUtcNow();
        var spec = new ExpiredHeldReservationsSpec(now);
        IReadOnlyList<Reservation> expired = await reservations.ListAsync(spec, enableTracking: true, ct).ConfigureAwait(false);

        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (Reservation reservation in expired)
        {
            reservation.Expire();

            foreach (ReservationLine line in reservation.Lines)
            {
                foreach (Allocation allocation in line.Allocations)
                {
                    StockItem? item = await stockItems.FirstOrDefaultAsync(
                        new StockItemByProductLocationSpec(reservation.TenantId, line.ProductId, allocation.LocationId),
                        enableTracking: true,
                        ct).ConfigureAwait(false);

                    item?.Release(allocation.Quantity);
                }
            }
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return expired.Count;
    }
}

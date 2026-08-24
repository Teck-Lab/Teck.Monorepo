using Inventories.Application.Inventory.ReadModels;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.FeatureFlags;
using Wolverine;

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
    /// <param name="bus">The message bus used for gated backorder expiry outcomes.</param>
    /// <param name="featureProvider">The lifecycle feature flag provider.</param>
    /// <param name="scopeFactory">The factory for a clean retry scope after a concurrency conflict.</param>
    /// <param name="inventoryOptions">The configured concurrency retry budget.</param>
    /// <returns>The number of reservations expired by this sweep.</returns>
    public static async Task<int> Handle(
        ExpireHeldReservationsCommand command,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct,
        IMessageBus? bus = null,
        IFeatureProvider? featureProvider = null,
        IServiceScopeFactory? scopeFactory = null,
        IOptions<InventoryOptions>? inventoryOptions = null)
    {
        try
        {
            return await AttemptAsync(command, reservations, stockItems, unitOfWork, timeProvider, ct, bus, featureProvider).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException) when (scopeFactory is not null)
        {
            // A failed save leaves the ambient graph stale; re-run the whole sweep from a fresh scope.
        }

        int maxRetries = inventoryOptions?.Value.MaxReserveRetries ?? 0;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            using IServiceScope scope = scopeFactory!.CreateScope();
            IServiceProvider services = scope.ServiceProvider;
            try
            {
                return await AttemptAsync(
                    command,
                    services.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
                    services.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
                    services.GetRequiredService<IUnitOfWork>(),
                    services.GetRequiredService<TimeProvider>(),
                    ct,
                    bus,
                    featureProvider ?? services.GetService<IFeatureProvider>()).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Retry with a separate change tracker.
            }
        }

        throw new DbUpdateConcurrencyException("Reservation expiry contention exhausted the configured retry budget.");
    }

    private static async Task<int> AttemptAsync(
        ExpireHeldReservationsCommand command,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct,
        IMessageBus? bus,
        IFeatureProvider? featureProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        DateTimeOffset now = timeProvider.GetUtcNow();
        var spec = new ExpiredHeldReservationsSpec(now);
        IReadOnlyList<Reservation> expired = await reservations.ListAsync(spec, enableTracking: true, ct).ConfigureAwait(false);

        if (expired.Count == 0)
        {
            return 0;
        }

        var expiredBackorders = new List<Reservation>();
        foreach (Reservation reservation in expired)
        {
            if (reservation.SourceType == ReservationSource.Order)
            {
                reservation.ExpireBackorder();
                if (reservation.IsLifecycleV2)
                {
                    expiredBackorders.Add(reservation);
                }
            }
            else
            {
                reservation.Expire();
            }

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

        if (bus is not null && featureProvider?.IsEnabled("CheckoutLifecycleV2") == true)
        {
            foreach (Reservation reservation in expiredBackorders)
            {
                await bus.PublishAsync(new BackorderExpiredIntegrationEvent
                {
                    OrderId = reservation.SourceId,
                    BasketId = reservation.BasketId,
                    TenantId = reservation.TenantId,
                    SourceCorrelationId = reservation.SourceCorrelationId,
                    IdempotencyKey = reservation.BackorderExpiredOutcomeKey!,
                    ExpiredAt = now,
                }).ConfigureAwait(false);
            }
        }

        return expired.Count;
    }
}

using Inventories.Application.Inventory.ReadModels;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Inventories.Application.Inventory.Features.ReleaseReservation.V1;

/// <summary>Handles idempotent release of active order and basket reservations.</summary>
public static class ReleaseReservationHandler
{
    /// <summary>Releases the requested reservations, commits once when anything changed, then publishes the release outcome.</summary>
    /// <param name="command">The release request.</param>
    /// <param name="reservations">The tracked reservation repository.</param>
    /// <param name="stockItems">The tracked stock repository.</param>
    /// <param name="unitOfWork">The single commit point.</param>
    /// <param name="bus">The message bus used to publish the outcome.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="scopeFactory">The factory for a clean retry scope after a concurrency conflict.</param>
    /// <param name="inventoryOptions">The configured concurrency retry budget.</param>
    /// <returns>A task representing the release.</returns>
    public static async Task Handle(
        ReleaseReservationCommand command,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct,
        IServiceScopeFactory? scopeFactory = null,
        IOptions<InventoryOptions>? inventoryOptions = null)
    {
        try
        {
            await AttemptAsync(command, reservations, stockItems, unitOfWork, bus, ct).ConfigureAwait(false);
            return;
        }
        catch (DbUpdateConcurrencyException) when (scopeFactory is not null)
        {
            // Do not reuse the failed scope: it contains stale reservation and allocation snapshots.
        }

        int maxRetries = inventoryOptions?.Value.MaxReserveRetries ?? 0;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            using IServiceScope scope = scopeFactory!.CreateScope();
            IServiceProvider services = scope.ServiceProvider;
            try
            {
                await AttemptAsync(
                    command,
                    services.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
                    services.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
                    services.GetRequiredService<IUnitOfWork>(),
                    bus,
                    ct).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Retry with freshly reloaded entities.
            }
        }

        throw new DbUpdateConcurrencyException("Reservation release contention exhausted the configured retry budget.");
    }

    internal static async Task AttemptAsync(
        ReleaseReservationCommand command,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        bool released = await ReleaseAsync(command, reservations, stockItems, ct).ConfigureAwait(false);
        if (!released)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await bus.PublishAsync(new StockReleasedIntegrationEvent
        {
            OrderId = command.OrderId,
            BasketId = command.BasketId,
            TenantId = command.TenantId,
            SourceCorrelationId = command.SourceCorrelationId,
            RequestId = command.RequestId,
        }).ConfigureAwait(false);
    }

    /// <summary>Mutates active matching reservations and their allocated stock without committing or publishing.</summary>
    internal static async Task<bool> ReleaseAsync(
        ReleaseReservationCommand command,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        CancellationToken ct)
    {
        var candidates = new List<Reservation?>
        {
            await reservations.FirstOrDefaultAsync(
                new ReservationBySourceSpec(command.TenantId, ReservationSource.Order, command.OrderId),
                enableTracking: true,
                ct).ConfigureAwait(false),
        };

        if (command.BasketId is Guid basketId)
        {
            candidates.Add(await reservations.FirstOrDefaultAsync(
                new ReservationBySourceSpec(command.TenantId, ReservationSource.Basket, basketId),
                enableTracking: true,
                ct).ConfigureAwait(false));
        }

        bool released = false;
        foreach (Reservation reservation in candidates.Where(candidate => candidate?.Status.IsActive == true).Cast<Reservation>())
        {
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

            reservation.Release();
            released = true;
        }

        return released;
    }
}

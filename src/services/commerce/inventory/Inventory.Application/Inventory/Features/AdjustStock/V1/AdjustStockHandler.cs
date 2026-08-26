using Inventories.Application.Inventory.Mapping;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.FeatureFlags;
using Wolverine;

namespace Inventories.Application.Inventory.Features.AdjustStock.V1;

/// <summary>Handles <see cref="AdjustStockCommand"/>.</summary>
public static class AdjustStockHandler
{
    /// <summary>
    /// Adjusts a stock item's quantity on hand and, on a positive adjust, fills outstanding
    /// backordered reservation lines for that product (FIFO, oldest reservation first) up to the
    /// newly-created availability — all within a single commit — then publishes any stock-level
    /// integration events triggered by the adjustment (depletion, replenishment, reorder threshold).
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The stock item write repository.</param>
    /// <param name="reservations">The reservation write repository, used to load and fill backorders.</param>
    /// <param name="unitOfWork">The unit of work (single commit point for the adjust and any backorder fill).</param>
    /// <param name="bus">The message bus used to publish integration events.</param>
    /// <param name="timeProvider">The clock used to decide whether a held reservation has expired.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="featureProvider">The lifecycle feature flag provider.</param>
    /// <param name="scopeFactory">The factory for a clean retry scope after a concurrency conflict.</param>
    /// <param name="inventoryOptions">The configured concurrency retry budget.</param>
    /// <returns>The adjusted stock item.</returns>
    public static async Task<StockItemDto> Handle(
        AdjustStockCommand command,
        IGenericWriteRepository<StockItem, Guid> repository,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider timeProvider,
        CancellationToken ct,
        IFeatureProvider? featureProvider = null,
        IServiceScopeFactory? scopeFactory = null,
        IOptions<InventoryOptions>? inventoryOptions = null)
    {
        try
        {
            return await AttemptAsync(command, repository, reservations, unitOfWork, bus, timeProvider, ct, featureProvider).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException) when (scopeFactory is not null)
        {
            // The failed ambient DbContext still tracks its locally-mutated reservation graph.
            // Retry only from a new scope so the next attempt reloads committed state.
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
                    services.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
                    services.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
                    services.GetRequiredService<IUnitOfWork>(),
                    bus,
                    services.GetRequiredService<TimeProvider>(),
                    ct,
                    featureProvider ?? services.GetService<IFeatureProvider>()).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Try again with another clean DbContext until the configured retry budget is exhausted.
            }
        }

        throw new DbUpdateConcurrencyException("Stock adjustment contention exhausted the configured retry budget.");
    }

    private static async Task<StockItemDto> AttemptAsync(
        AdjustStockCommand command,
        IGenericWriteRepository<StockItem, Guid> repository,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider timeProvider,
        CancellationToken ct,
        IFeatureProvider? featureProvider)
    {
        var item = await repository.FirstOrDefaultAsync(new StockItemByIdSpec(command.StockItemId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Stock item '{command.StockItemId}' was not found.");

        bool wasDepleted = item.IsDepleted();
        bool wasBelowReorder = item.CrossedReorderThreshold();

        item.Adjust(command.Delta);

        var readyBackorders = new List<Reservation>();
        if (command.Delta > 0)
        {
            await FillBackordersAsync(item, reservations, timeProvider, readyBackorders, ct).ConfigureAwait(false);
        }

        // Publish integration events only after the commit succeeds. Publishing directly here
        // (rather than via an EF -> Wolverine domain-event bridge, which is not wired platform-wide)
        // mirrors the basket service's CheckoutHandler pattern. The backorder fill above runs
        // BEFORE this single SaveChangesAsync, so the stock adjustment and the reservations it
        // fills commit atomically — stock can never be persisted as replenished without the
        // backorder it just covered also being persisted as filled, and vice versa.
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        if (featureProvider?.IsEnabled("CheckoutLifecycleV2") == true)
        {
            foreach (Reservation reservation in readyBackorders)
            {
                await bus.PublishAsync(new BackorderReadyIntegrationEvent
                {
                    OrderId = reservation.SourceId,
                    BasketId = reservation.BasketId,
                    TenantId = reservation.TenantId,
                    SourceCorrelationId = reservation.SourceCorrelationId,
                    IdempotencyKey = reservation.BackorderReadyOutcomeKey!,
                    ReadyAt = timeProvider.GetUtcNow(),
                }).ConfigureAwait(false);
            }
        }

        if (command.Delta > 0 && wasDepleted && !item.IsDepleted())
        {
            await bus.PublishAsync(new StockReplenishedIntegrationEvent
            {
                ProductId = item.ProductId,
                LocationId = item.LocationId,
                TenantId = item.TenantId,
                Available = item.Available,
            }).ConfigureAwait(false);
        }

        if (!wasDepleted && item.IsDepleted())
        {
            await bus.PublishAsync(new StockDepletedIntegrationEvent
            {
                ProductId = item.ProductId,
                LocationId = item.LocationId,
                TenantId = item.TenantId,
                Available = item.Available,
            }).ConfigureAwait(false);
        }

        // Fire only on the downward crossing into the reorder zone, not on every adjust while
        // already below the threshold — otherwise each subsequent adjustment re-spams the event.
        if (!wasBelowReorder && item.CrossedReorderThreshold())
        {
            await bus.PublishAsync(new ReorderTriggeredIntegrationEvent
            {
                ProductId = item.ProductId,
                LocationId = item.LocationId,
                TenantId = item.TenantId,
                Available = item.Available,
                ReorderThreshold = item.ReorderThreshold,
            }).ConfigureAwait(false);
        }

        return item.ToDto();
    }

    /// <summary>
    /// Loads active reservations carrying an outstanding backordered line for
    /// <paramref name="item"/>'s product (oldest first, via <see cref="BackorderedLinesByProductSpec"/>),
    /// and converts as much backordered quantity into real allocations at this item's location as the
    /// newly-created availability covers, re-consuming that availability via
    /// <see cref="StockItem.Reserve(int)"/> as each line is filled. Stops once availability is
    /// exhausted or no backordered lines remain.
    /// </summary>
    /// <param name="item">The stock item that was just positively adjusted.</param>
    /// <param name="reservations">The reservation write repository.</param>
    /// <param name="timeProvider">The clock used to decide whether a held reservation has expired.</param>
    /// <param name="readyBackorders">The order reservations that transition to fully allocated.</param>
    /// <param name="ct">A cancellation token.</param>
    private static async Task FillBackordersAsync(
        StockItem item,
        IGenericWriteRepository<Reservation, Guid> reservations,
        TimeProvider timeProvider,
        ICollection<Reservation> readyBackorders,
        CancellationToken ct)
    {
        if (item.Available <= 0)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var spec = new BackorderedLinesByProductSpec(item.TenantId, item.ProductId, now);
        IReadOnlyList<Reservation> candidates = await reservations.ListAsync(spec, enableTracking: true, ct).ConfigureAwait(false);

        foreach (Reservation reservation in candidates)
        {
            if (item.Available <= 0)
            {
                break;
            }

            ReservationLine? line = reservation.Lines.FirstOrDefault(
                candidate => candidate.ProductId == item.ProductId && candidate.BackorderedQuantity > 0);
            if (line is null)
            {
                continue;
            }

            int fillQuantity = Math.Min(item.Available, line.BackorderedQuantity);

            bool becameReady = reservation.FillBackorder(item.ProductId, item.LocationId, fillQuantity);
            item.Reserve(fillQuantity);

            if (becameReady && reservation.SourceType == ReservationSource.Order && reservation.IsLifecycleV2)
            {
                readyBackorders.Add(reservation);
            }
        }
    }
}

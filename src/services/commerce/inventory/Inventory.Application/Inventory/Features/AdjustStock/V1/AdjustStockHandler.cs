using Inventories.Application.Inventory.Mapping;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
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
    /// <returns>The adjusted stock item.</returns>
    public static async Task<StockItemDto> Handle(
        AdjustStockCommand command,
        IGenericWriteRepository<StockItem, Guid> repository,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var item = await repository.FirstOrDefaultAsync(new StockItemByIdSpec(command.StockItemId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Stock item '{command.StockItemId}' was not found.");

        bool wasDepleted = item.IsDepleted();
        bool wasBelowReorder = item.CrossedReorderThreshold();

        item.Adjust(command.Delta);

        if (command.Delta > 0)
        {
            await FillBackordersAsync(item, reservations, timeProvider, ct).ConfigureAwait(false);
        }

        // Publish integration events only after the commit succeeds. Publishing directly here
        // (rather than via an EF -> Wolverine domain-event bridge, which is not wired platform-wide)
        // mirrors the basket service's CheckoutHandler pattern. The backorder fill above runs
        // BEFORE this single SaveChangesAsync, so the stock adjustment and the reservations it
        // fills commit atomically — stock can never be persisted as replenished without the
        // backorder it just covered also being persisted as filled, and vice versa.
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

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
    /// <param name="ct">A cancellation token.</param>
    private static async Task FillBackordersAsync(
        StockItem item,
        IGenericWriteRepository<Reservation, Guid> reservations,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (item.Available <= 0)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var spec = new BackorderedLinesByProductSpec(item.ProductId, now);
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

            reservation.FillBackorder(item.ProductId, item.LocationId, fillQuantity);
            item.Reserve(fillQuantity);
        }
    }
}

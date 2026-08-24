using Inventories.Application.Inventory.Features.CommitReservation.V1;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Inventories.Application.Inventory.EventHandlers.IntegrationEvents;

/// <summary>Commits stock reservations in response to an order being placed.</summary>
public static class OrderPlacedHandler
{
    /// <summary>
    /// Consumes <see cref="OrderPlacedIntegrationEvent"/>: idempotently reserves stock for the order
    /// (all-or-nothing, with optimistic-concurrency retry) and, only after the commit succeeds,
    /// publishes the resulting stock integration events. A re-delivered order is a no-op.
    /// </summary>
    /// <param name="evt">The order-placed event.</param>
    /// <param name="stockItems">The stock write repository.</param>
    /// <param name="reservations">The reservation write repository.</param>
    /// <param name="locationPriorities">The location-priority read repository.</param>
    /// <param name="unitOfWork">The unit of work (single commit point).</param>
    /// <param name="scopeFactory">Factory used by the committer to open a fresh scope per retry.</param>
    /// <param name="options">The inventory options, providing the reserve retry budget.</param>
    /// <param name="bus">The message bus used to publish integration events after commit.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="timeProvider">The clock used to calculate the bounded backorder deadline.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task Handle(
        OrderPlacedIntegrationEvent evt,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericReadRepository<LocationPriority, Guid> locationPriorities,
        IUnitOfWork unitOfWork,
        IServiceScopeFactory scopeFactory,
        IOptions<InventoryOptions> options,
        IMessageBus bus,
        CancellationToken ct,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(evt);
        timeProvider ??= TimeProvider.System;

        var request = new ReservationCommitRequest(
            ReservationSource.Order,
            evt.OrderId,
            evt.TenantId,
            evt.Lines.Select(line => new ReservationRequestLine(line.ProductId, line.Quantity)).ToList());

        ReservationCommitResult result = await ReservationCommitter.CommitAsync(
            stockItems,
            reservations,
            locationPriorities,
            unitOfWork,
            scopeFactory,
            options.Value.MaxReserveRetries,
            request,
            timeProvider.GetUtcNow() + options.Value.BackorderWait,
            ct).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case ReservationCommitOutcome.AlreadyReserved:
                // Idempotent re-delivery: nothing committed, nothing to publish.
                return;

            case ReservationCommitOutcome.Rejected:
            case ReservationCommitOutcome.Contention:
                await bus.PublishAsync(new StockReservationRejectedIntegrationEvent
                {
                    ReservationId = result.ReservationId,
                    SourceType = ReservationSource.Order.Name,
                    SourceId = evt.OrderId,
                    TenantId = evt.TenantId,
                    Lines = result.Lines,
                }).ConfigureAwait(false);
                return;

            case ReservationCommitOutcome.Committed:
                await PublishCommittedAsync(evt, result, bus).ConfigureAwait(false);
                return;

            default:
                return;
        }
    }

    private static async Task PublishCommittedAsync(
        OrderPlacedIntegrationEvent evt,
        ReservationCommitResult result,
        IMessageBus bus)
    {
        await bus.PublishAsync(new StockReservedIntegrationEvent
        {
            ReservationId = result.ReservationId,
            SourceType = ReservationSource.Order.Name,
            SourceId = evt.OrderId,
            TenantId = evt.TenantId,
            Lines = result.Lines,
        }).ConfigureAwait(false);

        foreach (AffectedStock stock in result.AffectedStock)
        {
            if (stock.NewlyDepleted)
            {
                await bus.PublishAsync(new StockDepletedIntegrationEvent
                {
                    ProductId = stock.ProductId,
                    LocationId = stock.LocationId,
                    TenantId = stock.TenantId,
                    Available = stock.Available,
                }).ConfigureAwait(false);
            }

            if (stock.NewlyReorderTriggered)
            {
                await bus.PublishAsync(new ReorderTriggeredIntegrationEvent
                {
                    ProductId = stock.ProductId,
                    LocationId = stock.LocationId,
                    TenantId = stock.TenantId,
                    Available = stock.Available,
                    ReorderThreshold = stock.ReorderThreshold,
                }).ConfigureAwait(false);
            }
        }
    }
}

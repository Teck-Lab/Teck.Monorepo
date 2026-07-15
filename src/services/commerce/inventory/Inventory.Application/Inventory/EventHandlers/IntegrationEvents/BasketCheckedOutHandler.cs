using Inventories.Application.Inventory.Features.CommitReservation.V1;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Inventories.Application.Inventory.EventHandlers.IntegrationEvents;

/// <summary>Places soft stock holds in response to a basket being checked out.</summary>
public static class BasketCheckedOutHandler
{
    /// <summary>
    /// Consumes <see cref="BasketCheckedOutIntegrationEvent"/>: idempotently places a
    /// <see cref="ReservationStatus.Held"/> reservation for the basket (all-or-nothing, with
    /// optimistic-concurrency retry) that expires after <see cref="InventoryOptions.HoldTtl"/> unless
    /// committed first, giving best-effort oversell protection during checkout. Only after the hold
    /// succeeds are the resulting stock integration events published. A re-delivered basket is a no-op.
    /// </summary>
    /// <param name="evt">The basket-checked-out event.</param>
    /// <param name="stockItems">The stock write repository.</param>
    /// <param name="reservations">The reservation write repository.</param>
    /// <param name="locationPriorities">The location-priority read repository.</param>
    /// <param name="unitOfWork">The unit of work (single commit point).</param>
    /// <param name="scopeFactory">Factory used by the committer to open a fresh scope per retry.</param>
    /// <param name="options">The inventory options, providing the hold TTL and reserve retry budget.</param>
    /// <param name="timeProvider">The clock used to compute the hold's expiry.</param>
    /// <param name="bus">The message bus used to publish integration events after the hold is placed.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task Handle(
        BasketCheckedOutIntegrationEvent evt,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericReadRepository<LocationPriority, Guid> locationPriorities,
        IUnitOfWork unitOfWork,
        IServiceScopeFactory scopeFactory,
        IOptions<InventoryOptions> options,
        TimeProvider timeProvider,
        IMessageBus bus,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var request = new ReservationCommitRequest(
            ReservationSource.Basket,
            evt.BasketId,
            evt.TenantId,
            evt.Items.Select(line => new ReservationRequestLine(line.ProductId, line.Quantity)).ToList());

        DateTimeOffset expiresAt = timeProvider.GetUtcNow() + options.Value.HoldTtl;

        ReservationCommitResult result = await ReservationCommitter.HoldForAsync(
            stockItems,
            reservations,
            locationPriorities,
            unitOfWork,
            scopeFactory,
            options.Value.MaxReserveRetries,
            request,
            expiresAt,
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
                    SourceType = ReservationSource.Basket.Name,
                    SourceId = evt.BasketId,
                    TenantId = evt.TenantId,
                    Lines = result.Lines,
                }).ConfigureAwait(false);
                return;

            case ReservationCommitOutcome.Committed:
                await PublishHeldAsync(evt, result, bus).ConfigureAwait(false);
                return;

            default:
                return;
        }
    }

    private static async Task PublishHeldAsync(
        BasketCheckedOutIntegrationEvent evt,
        ReservationCommitResult result,
        IMessageBus bus)
    {
        await bus.PublishAsync(new StockReservedIntegrationEvent
        {
            ReservationId = result.ReservationId,
            SourceType = ReservationSource.Basket.Name,
            SourceId = evt.BasketId,
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

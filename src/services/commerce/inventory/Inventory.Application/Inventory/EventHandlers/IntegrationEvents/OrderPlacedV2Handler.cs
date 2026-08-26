using Inventories.Application.Inventory.Features.CommitReservation.V1;
using Inventories.Application.Inventory.Features.ReleaseReservation.V1;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.FeatureFlags;
using Wolverine;

namespace Inventories.Application.Inventory.EventHandlers.IntegrationEvents;

/// <summary>Consumes the version-two order placement contract while retaining version-one outcome compatibility.</summary>
public static class OrderPlacedV2Handler
{
    /// <summary>Reserves stock for a lifecycle order and publishes the appropriate versioned outcome after its commit.</summary>
    /// <param name="evt">The version-two order placement event.</param>
    /// <param name="stockItems">The tracked stock repository.</param>
    /// <param name="reservations">The tracked reservation repository.</param>
    /// <param name="locationPriorities">The location-priority repository.</param>
    /// <param name="unitOfWork">The single commit point.</param>
    /// <param name="scopeFactory">The factory used for fresh concurrency retry scopes.</param>
    /// <param name="options">The inventory options.</param>
    /// <param name="timeProvider">The clock used for the backorder deadline.</param>
    /// <param name="featureProvider">The lifecycle feature flag provider.</param>
    /// <param name="bus">The message bus used to publish outcomes.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the reservation attempt.</returns>
    public static async Task Handle(
        OrderPlacedV2IntegrationEvent evt,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericReadRepository<LocationPriority, Guid> locationPriorities,
        IUnitOfWork unitOfWork,
        IServiceScopeFactory scopeFactory,
        IOptions<InventoryOptions> options,
        TimeProvider timeProvider,
        IFeatureProvider featureProvider,
        IMessageBus bus,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfEqual(evt.BasketId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(evt.SourceCorrelationId);

        var request = new ReservationCommitRequest(
            ReservationSource.Order,
            evt.OrderId,
            evt.TenantId,
            evt.Lines.Select(line => new ReservationRequestLine(line.ProductId, line.Quantity)).ToList(),
            evt.BasketId,
            evt.SourceCorrelationId);

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
                return;
            case ReservationCommitOutcome.LifecycleHandoff:
                await PublishLifecycleHandoffAsync(evt, result, featureProvider, bus).ConfigureAwait(false);
                return;
            case ReservationCommitOutcome.Rejected:
                if (result.RequiresFreshScopeRejectionHandling)
                {
                    await PublishRejectedInFreshScopeAsync(evt, result, scopeFactory, bus, ct).ConfigureAwait(false);
                }
                else
                {
                    await PublishRejectedAsync(evt, result, reservations, stockItems, unitOfWork, featureProvider, bus, ct).ConfigureAwait(false);
                }

                return;
            case ReservationCommitOutcome.Contention:
                await PublishRejectedInFreshScopeAsync(evt, result, scopeFactory, bus, ct).ConfigureAwait(false);
                return;
            case ReservationCommitOutcome.Committed:
                await PublishCommittedAsync(evt, result, featureProvider, bus).ConfigureAwait(false);
                return;
        }
    }

    private static async Task PublishRejectedAsync(
        OrderPlacedV2IntegrationEvent evt,
        ReservationCommitResult result,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IUnitOfWork unitOfWork,
        IFeatureProvider featureProvider,
        IMessageBus bus,
        CancellationToken ct)
    {
        bool released = await ReleaseReservationHandler.ReleaseAsync(
            new ReleaseReservationCommand(evt.OrderId, evt.BasketId, evt.TenantId, evt.SourceCorrelationId, $"stock-rejected:{evt.OrderId:N}"),
            reservations,
            stockItems,
            ct).ConfigureAwait(false);
        if (released)
        {
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await bus.PublishAsync(new StockReservationRejectedIntegrationEvent
        {
            ReservationId = result.ReservationId,
            SourceType = ReservationSource.Order.Name,
            SourceId = evt.OrderId,
            TenantId = evt.TenantId,
            Lines = result.Lines,
        }).ConfigureAwait(false);

        if (featureProvider.IsEnabled("CheckoutLifecycleV2"))
        {
            await bus.PublishAsync(new StockReservationRejectedV2IntegrationEvent
            {
                ReservationId = result.ReservationId,
                OrderId = evt.OrderId,
                BasketId = evt.BasketId,
                SourceType = ReservationSource.Order.Name,
                SourceId = evt.OrderId,
                SourceCorrelationId = evt.SourceCorrelationId,
                TenantId = evt.TenantId,
                IdempotencyKey = $"stock-rejected:{evt.OrderId:N}",
                Lines = result.Lines.ToList(),
            }).ConfigureAwait(false);
        }
    }

    private static async Task PublishRejectedInFreshScopeAsync(
        OrderPlacedV2IntegrationEvent evt,
        ReservationCommitResult result,
        IServiceScopeFactory scopeFactory,
        IMessageBus bus,
        CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        await PublishRejectedAsync(
            evt,
            result,
            services.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
            services.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IFeatureProvider>(),
            bus,
            ct).ConfigureAwait(false);
    }

    private static async Task PublishCommittedAsync(
        OrderPlacedV2IntegrationEvent evt,
        ReservationCommitResult result,
        IFeatureProvider featureProvider,
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

        if (featureProvider.IsEnabled("CheckoutLifecycleV2"))
        {
            await bus.PublishAsync(new StockReservedV2IntegrationEvent
            {
                ReservationId = result.ReservationId,
                OrderId = evt.OrderId,
                BasketId = evt.BasketId,
                SourceType = ReservationSource.Order.Name,
                SourceId = evt.OrderId,
                SourceCorrelationId = evt.SourceCorrelationId,
                TenantId = evt.TenantId,
                IdempotencyKey = $"stock-reserved:{evt.OrderId:N}",
                Lines = result.Lines.ToList(),
            }).ConfigureAwait(false);
        }

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

    private static async Task PublishLifecycleHandoffAsync(
        OrderPlacedV2IntegrationEvent evt,
        ReservationCommitResult result,
        IFeatureProvider featureProvider,
        IMessageBus bus)
    {
        if (!featureProvider.IsEnabled("CheckoutLifecycleV2"))
        {
            return;
        }

        await bus.PublishAsync(new StockReservedV2IntegrationEvent
        {
            ReservationId = result.ReservationId,
            OrderId = evt.OrderId,
            BasketId = evt.BasketId,
            SourceType = ReservationSource.Order.Name,
            SourceId = evt.OrderId,
            SourceCorrelationId = evt.SourceCorrelationId,
            TenantId = evt.TenantId,
            IdempotencyKey = $"stock-reserved:{evt.OrderId:N}",
            Lines = result.Lines.ToList(),
        }).ConfigureAwait(false);
    }
}

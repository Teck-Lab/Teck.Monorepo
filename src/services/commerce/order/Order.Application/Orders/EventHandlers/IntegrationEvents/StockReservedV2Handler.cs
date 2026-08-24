using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Reconciles successful stock reservations.</summary>
public static class StockReservedV2Handler
{
    /// <summary>Applies a V2 stock reservation.</summary>
    /// <param name="evt">The version-two reservation outcome.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after reconciliation.</returns>
    public static Task Handle(StockReservedV2IntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct) =>
        Apply(evt.OrderId ?? evt.SourceId, evt.TenantId, evt.IdempotencyKey, evt.SourceCorrelationId, evt.Lines.Any(line => line.BackorderedQuantity > 0), orders, unitOfWork, bus, ct);

    /// <summary>Applies a frozen V1 stock reservation through the same transition.</summary>
    /// <param name="evt">The legacy reservation outcome.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after reconciliation.</returns>
    public static Task Handle(StockReservedIntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct) =>
        Apply(evt.SourceId, evt.TenantId, $"legacy-stock-reserved:{evt.ReservationId:N}", evt.Id.ToString("N"), hasOutstandingBackorder: false, orders, unitOfWork, bus, ct);

    private static async Task Apply(Guid orderId, string tenantId, string key, string correlation, bool hasOutstandingBackorder, IGenericWriteRepository<Order, Guid> orders, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new ReadModels.OrderByIdSpec(orderId), enableTracking: true, ct).ConfigureAwait(false);
        if (order is null || !string.Equals(order.TenantId, tenantId, StringComparison.Ordinal))
        {
            return;
        }

        var notification = order.ApplyStockReserved(hasOutstandingBackorder, key, correlation);
        var readyKey = string.Empty;
        var readyCorrelation = string.Empty;
        var hasPendingReady = hasOutstandingBackorder && order.TryConsumePendingBackorderReady(out readyKey, out readyCorrelation);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        if (hasPendingReady)
        {
            await BackorderReadyHandler.PublishPriceCheckAsync(order, readyKey, readyCorrelation, bus).ConfigureAwait(false);
        }

        await PaymentCapturedV2Handler.OrderLifecycleEvents.PublishAsync(notification, bus).ConfigureAwait(false);
    }
}

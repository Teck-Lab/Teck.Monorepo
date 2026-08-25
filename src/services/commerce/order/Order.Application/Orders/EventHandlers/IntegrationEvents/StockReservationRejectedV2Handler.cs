using Finbuckle.MultiTenant.Abstractions;
using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Reconciles rejected stock and requests safe release/cancellation.</summary>
public static class StockReservationRejectedV2Handler
{
    /// <summary>Applies a V2 stock rejection.</summary>
    /// <param name="evt">The version-two rejection outcome.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="tenant">The tenant established from the Wolverine envelope.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after reconciliation.</returns>
    public static Task Handle(StockReservationRejectedV2IntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, ITenantInfo tenant, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct) =>
        Apply(evt.OrderId ?? evt.SourceId, evt.TenantId, evt.IdempotencyKey, evt.SourceCorrelationId, orders, tenant, unitOfWork, bus, ct);

    /// <summary>Applies a frozen V1 stock rejection.</summary>
    /// <param name="evt">The legacy rejection outcome.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="tenant">The tenant established from the Wolverine envelope.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after reconciliation.</returns>
    public static Task Handle(StockReservationRejectedIntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, ITenantInfo tenant, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct) =>
        Apply(evt.SourceId, evt.TenantId, $"legacy-stock-rejected:{evt.ReservationId:N}", evt.Id.ToString("N"), orders, tenant, unitOfWork, bus, ct);

    private static async Task Apply(Guid orderId, string payloadTenantId, string key, string correlation, IGenericWriteRepository<Order, Guid> orders, ITenantInfo tenant, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        OrderEventTenantGuard.EnsureMatchesEnvelope(payloadTenantId, tenant);

        var order = await orders.FirstOrDefaultAsync(new ReadModels.OrderByIdSpec(orderId, tenant.Id), enableTracking: true, ct).ConfigureAwait(false);
        if (order is null || !string.Equals(order.TenantId, tenant.Id, StringComparison.Ordinal))
        {
            return;
        }

        var notification = order.ApplyStockRejected(key, correlation, "Your order could not be supplied.");
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        if (notification is not null)
        {
            await bus.PublishAsync(new StockReleaseRequestedIntegrationEvent { OrderId = order.Id, BasketId = order.BasketId, TenantId = order.TenantId, SourceCorrelationId = correlation, RequestId = $"stock-release:{key}" }).ConfigureAwait(false);
            await bus.PublishAsync(new PaymentCancellationRequestedIntegrationEvent { OrderId = order.Id, PaymentId = order.PaymentId, TenantId = order.TenantId, RequestId = $"payment-cancel:{key}", SourceCorrelationId = correlation }).ConfigureAwait(false);
        }

        await PaymentCapturedV2Handler.OrderLifecycleEvents.PublishAsync(notification, bus).ConfigureAwait(false);
    }
}

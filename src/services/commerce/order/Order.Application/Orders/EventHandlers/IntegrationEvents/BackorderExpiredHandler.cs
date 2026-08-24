using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Reconciles an expired order backorder into one safe terminal outcome.</summary>
public static class BackorderExpiredHandler
{
    /// <summary>Applies expiry and requests payment/stock cleanup where applicable.</summary>
    /// <param name="evt">The backorder-expired outcome.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after cleanup and notification publication.</returns>
    public static async Task Handle(BackorderExpiredIntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new ReadModels.OrderByIdSpec(evt.OrderId), enableTracking: true, ct).ConfigureAwait(false);
        if (order is null || !string.Equals(order.TenantId, evt.TenantId, StringComparison.Ordinal))
        {
            return;
        }

        var notification = order.ApplyBackorderExpired(evt.IdempotencyKey, evt.SourceCorrelationId);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        if (notification is not null)
        {
            await bus.PublishAsync(new StockReleaseRequestedIntegrationEvent { OrderId = order.Id, BasketId = order.BasketId, TenantId = order.TenantId, SourceCorrelationId = evt.SourceCorrelationId, RequestId = $"stock-release:{evt.IdempotencyKey}" }).ConfigureAwait(false);
            await bus.PublishAsync(new PaymentCancellationRequestedIntegrationEvent { OrderId = order.Id, PaymentId = order.PaymentId, TenantId = order.TenantId, RequestId = $"payment-cancel:{evt.IdempotencyKey}", SourceCorrelationId = evt.SourceCorrelationId }).ConfigureAwait(false);
        }

        await PaymentCapturedV2Handler.OrderLifecycleEvents.PublishAsync(notification, bus).ConfigureAwait(false);
    }
}

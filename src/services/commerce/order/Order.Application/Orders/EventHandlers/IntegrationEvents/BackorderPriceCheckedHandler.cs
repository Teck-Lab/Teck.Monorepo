using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Reconciles an authoritative backorder price result against the immutable ceiling.</summary>
public static class BackorderPriceCheckedHandler
{
    /// <summary>Confirms stock within the ceiling or safely rejects and releases it.</summary>
    /// <param name="evt">The authoritative price-check result.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the transition and any cleanup publication.</returns>
    public static async Task Handle(BackorderPriceCheckedIntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new ReadModels.OrderByIdSpec(evt.OrderId), enableTracking: true, ct).ConfigureAwait(false);
        if (order is null || !string.Equals(order.TenantId, evt.TenantId, StringComparison.Ordinal))
        {
            return;
        }

        if (evt.Amount <= 0 ||
            evt.AuthorizedAmount != order.AuthorizedAmount ||
            !string.Equals(evt.Currency, order.Currency, StringComparison.Ordinal))
        {
            return;
        }

        var notification = order.ApplyBackorderPriceChecked(evt.IsWithinAuthorizedAmount && evt.Amount <= order.AuthorizedAmount, evt.RequestId, evt.SourceCorrelationId);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        if ((!evt.IsWithinAuthorizedAmount || evt.Amount > order.AuthorizedAmount) && notification is not null)
        {
            await bus.PublishAsync(new StockReleaseRequestedIntegrationEvent { OrderId = order.Id, BasketId = order.BasketId, TenantId = order.TenantId, SourceCorrelationId = evt.SourceCorrelationId, RequestId = $"stock-release:{evt.RequestId}" }).ConfigureAwait(false);
            await bus.PublishAsync(new PaymentCancellationRequestedIntegrationEvent { OrderId = order.Id, PaymentId = order.PaymentId, TenantId = order.TenantId, RequestId = $"payment-cancel:{evt.RequestId}", SourceCorrelationId = evt.SourceCorrelationId }).ConfigureAwait(false);
        }

        await PaymentCapturedV2Handler.OrderLifecycleEvents.PublishAsync(notification, bus).ConfigureAwait(false);
    }
}

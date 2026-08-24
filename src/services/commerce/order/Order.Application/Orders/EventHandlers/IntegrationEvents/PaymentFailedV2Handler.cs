using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Reconciles safe payment failures through one idempotent transition.</summary>
public static class PaymentFailedV2Handler
{
    /// <summary>Applies a V2 failure outcome.</summary>
    /// <param name="evt">The version-two payment failure.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after reconciliation.</returns>
    public static Task Handle(PaymentFailedV2IntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct) =>
        Apply(evt.OrderId, evt.TenantId, evt.DeclineCategory, evt.ActionText, evt.RequestId, evt.SourceCorrelationId, orders, unitOfWork, bus, ct);

    /// <summary>Applies a frozen V1 failure as a generic safe decline.</summary>
    /// <param name="evt">The legacy payment failure.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after reconciliation.</returns>
    public static Task Handle(PaymentFailedIntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct) =>
        Apply(evt.OrderId, evt.TenantId, "generic-decline", "Please provide another payment method.", $"legacy-payment-failed:{evt.PaymentId:N}", evt.Id.ToString("N"), orders, unitOfWork, bus, ct);

    private static async Task Apply(Guid orderId, string tenantId, string category, string actionText, string key, string correlation, IGenericWriteRepository<Order, Guid> orders, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new ReadModels.OrderByIdSpec(orderId), enableTracking: true, ct).ConfigureAwait(false);
        if (order is null || !string.Equals(order.TenantId, tenantId, StringComparison.Ordinal))
        {
            return;
        }

        var notification = order.ApplyPaymentFailure(category, actionText, key, correlation);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await PaymentCapturedV2Handler.OrderLifecycleEvents.PublishAsync(notification, bus).ConfigureAwait(false);
    }
}

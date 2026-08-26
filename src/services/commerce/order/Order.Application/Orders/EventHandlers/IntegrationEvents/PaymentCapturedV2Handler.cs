using Finbuckle.MultiTenant.Abstractions;
using Orders.Domain.DomainEvents;
using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Reconciles immediate, delayed, and legacy successful payment outcomes.</summary>
public static class PaymentCapturedV2Handler
{
    /// <summary>Applies a V2 capture outcome.</summary>
    /// <param name="evt">The version-two payment outcome.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="tenant">The tenant established from the Wolverine envelope.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after reconciliation.</returns>
    public static Task Handle(PaymentCapturedV2IntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, ITenantInfo tenant, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct) =>
        Apply(evt.OrderId, evt.TenantId, evt.PaymentId, evt.Amount, evt.AuthorizedAmount, evt.Currency, evt.RequestId, evt.SourceCorrelationId, orders, tenant, unitOfWork, bus, ct);

    /// <summary>Applies a frozen V1 capture outcome through the same transition.</summary>
    /// <param name="evt">The legacy payment outcome.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="tenant">The tenant established from the Wolverine envelope.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after reconciliation.</returns>
    public static Task Handle(PaymentCapturedIntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, ITenantInfo tenant, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct) =>
        Apply(evt.OrderId, evt.TenantId, evt.PaymentId, evt.Amount, authorizedAmount: null, currency: null, $"legacy-payment-captured:{evt.PaymentId:N}", evt.Id.ToString("N"), orders, tenant, unitOfWork, bus, ct);

    private static async Task Apply(Guid orderId, string payloadTenantId, Guid paymentId, decimal amount, decimal? authorizedAmount, string? currency, string key, string correlation, IGenericWriteRepository<Order, Guid> orders, ITenantInfo tenant, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        OrderEventTenantGuard.EnsureMatchesEnvelope(payloadTenantId, tenant);

        var order = await orders.FirstOrDefaultAsync(new ReadModels.OrderByIdSpec(orderId, tenant.Id), enableTracking: true, ct).ConfigureAwait(false);
        if (order is null || !string.Equals(order.TenantId, tenant.Id, StringComparison.Ordinal))
        {
            return;
        }

        if (amount <= 0 || amount > order.AuthorizedAmount ||
            authorizedAmount is decimal eventAuthorizedAmount && eventAuthorizedAmount != order.AuthorizedAmount ||
            currency is not null && !string.Equals(currency, order.Currency, StringComparison.Ordinal))
        {
            return;
        }

        var notification = order.ApplyPaymentCaptured(paymentId, amount, key, correlation);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await OrderLifecycleEvents.PublishAsync(notification, bus).ConfigureAwait(false);
    }

    /// <summary>Maps lifecycle-domain notifications to their safe integration events.</summary>
    internal static class OrderLifecycleEvents
    {
    /// <summary>Publishes a lifecycle event when a new transition produced one.</summary>
        public static ValueTask PublishAsync(object? domainEvent, IMessageBus bus) => domainEvent switch
        {
            OrderConfirmed evt => bus.PublishAsync(new OrderConfirmedIntegrationEvent { OrderId = evt.OrderId, CustomerId = evt.CustomerId, KeycloakSubjectId = evt.KeycloakSubjectId, TenantId = evt.TenantId, Amount = evt.Amount, Currency = evt.Currency, IdempotencyKey = evt.IdempotencyKey, SourceCorrelationId = evt.SourceCorrelationId, AuthorizedAmount = evt.AuthorizedAmount }),
            OrderPaymentActionRequired evt => bus.PublishAsync(new OrderPaymentActionRequiredIntegrationEvent { OrderId = evt.OrderId, CustomerId = evt.CustomerId, KeycloakSubjectId = evt.KeycloakSubjectId, TenantId = evt.TenantId, DeclineCategory = evt.DeclineCategory, ActionText = evt.ActionText, IdempotencyKey = evt.IdempotencyKey, SourceCorrelationId = evt.SourceCorrelationId }),
            OrderCancelled evt => bus.PublishAsync(new OrderCancelledIntegrationEvent { OrderId = evt.OrderId, CustomerId = evt.CustomerId, KeycloakSubjectId = evt.KeycloakSubjectId, TenantId = evt.TenantId, ActionText = evt.ActionText, IdempotencyKey = evt.IdempotencyKey, SourceCorrelationId = evt.SourceCorrelationId }),
            OrderRejected evt => bus.PublishAsync(new OrderRejectedIntegrationEvent { OrderId = evt.OrderId, CustomerId = evt.CustomerId, KeycloakSubjectId = evt.KeycloakSubjectId, TenantId = evt.TenantId, FailureCategory = evt.FailureCategory, ActionText = evt.ActionText, IdempotencyKey = evt.IdempotencyKey, SourceCorrelationId = evt.SourceCorrelationId }),
            OrderBackorderOutcome evt => bus.PublishAsync(new OrderBackorderOutcomeIntegrationEvent { OrderId = evt.OrderId, CustomerId = evt.CustomerId, KeycloakSubjectId = evt.KeycloakSubjectId, TenantId = evt.TenantId, Outcome = evt.Outcome, ActionText = evt.ActionText, IdempotencyKey = evt.IdempotencyKey, SourceCorrelationId = evt.SourceCorrelationId }),
            _ => ValueTask.CompletedTask,
        };
    }
}

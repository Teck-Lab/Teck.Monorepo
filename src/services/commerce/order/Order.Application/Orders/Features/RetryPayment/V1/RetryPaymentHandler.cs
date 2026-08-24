using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using Orders.Application.Orders.ReadModels;
using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.Features.RetryPayment.V1;

/// <summary>Publishes one idempotent payment retry only for the persisted subject owner.</summary>
public static class RetryPaymentHandler
{
    /// <summary>Authorizes, persists, and publishes the retry request.</summary>
    /// <param name="command">The retry command with token and request id.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="identity">The current owner identity accessor.</param>
    /// <param name="tenant">The current tenant context.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A success result or a visible validation/not-found error.</returns>
    public static async Task<ErrorOr<Success>> Handle(RetryPaymentCommand command, IGenericWriteRepository<Order, Guid> orders, IOrderIdentityAccessor identity, ITenantInfo tenant, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RequestId) || string.IsNullOrWhiteSpace(command.PaymentMethodToken))
        {
            return Error.Validation("order.retry.invalid", "A request id and payment token are required.");
        }

        var order = await orders.FirstOrDefaultAsync(new OrderByIdSpec(command.OrderId), enableTracking: true, ct).ConfigureAwait(false);
        if (order is null || !string.Equals(order.TenantId, tenant.Id, StringComparison.Ordinal))
        {
            return Error.NotFound("order.not_found", "The requested order was not found.");
        }

        OrderOwnership.EnsureOwnedBy(order, identity);
        if (!order.CanRetryPayment)
        {
            return order.HasRecordedRetryRequest(command.RequestId)
                ? Result.Success
                : Error.Validation("order.retry.ineligible", "Payment retry is unavailable for the order's current state.");
        }

        if (!order.BeginRetry(command.RequestId))
        {
            return Result.Success;
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await bus.PublishAsync(new PaymentRetryRequestedIntegrationEvent { OrderId = order.Id, TenantId = order.TenantId, AuthorizedAmount = order.AuthorizedAmount, Currency = order.Currency, PaymentMethodToken = command.PaymentMethodToken, RequestId = command.RequestId, SourceCorrelationId = order.CheckoutCorrelationId }).ConfigureAwait(false);
        return Result.Success;
    }
}

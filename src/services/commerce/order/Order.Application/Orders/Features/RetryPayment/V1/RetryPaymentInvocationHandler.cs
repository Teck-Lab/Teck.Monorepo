using Finbuckle.MultiTenant.Abstractions;
using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using Wolverine;

namespace Orders.Application.Orders.Features.RetryPayment.V1;

/// <summary>Adapts payment retry errors into a Wolverine-returnable typed outcome.</summary>
public static class RetryPaymentInvocationHandler
{
    /// <summary>Invokes the payment retry lifecycle handler and classifies its outcome.</summary>
    /// <param name="command">The retry invocation command.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="identity">The current owner identity accessor.</param>
    /// <param name="tenant">The current tenant context.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">A token used to cancel the operation.</param>
    /// <returns>A typed retry invocation outcome.</returns>
    public static async Task<RetryPaymentInvocationResult> Handle(
        RetryPaymentInvocationCommand command,
        IGenericWriteRepository<Order, Guid> orders,
        IOrderIdentityAccessor identity,
        ITenantInfo tenant,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var result = await RetryPaymentHandler.Handle(
            new RetryPaymentCommand(command.OrderId, command.RequestId, command.PaymentMethodToken),
            orders,
            identity,
            tenant,
            unitOfWork,
            bus,
            ct).ConfigureAwait(false);

        if (!result.IsError)
        {
            return new RetryPaymentInvocationResult(RetryPaymentInvocationOutcome.Accepted);
        }

        return new RetryPaymentInvocationResult(result.Errors.Any(static error => error.Type == ErrorOr.ErrorType.NotFound)
            ? RetryPaymentInvocationOutcome.NotFound
            : RetryPaymentInvocationOutcome.Invalid);
    }
}

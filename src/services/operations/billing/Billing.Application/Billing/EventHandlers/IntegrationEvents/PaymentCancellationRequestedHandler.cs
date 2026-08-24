using Billings.Application.Billing.Payments.Features.CancelPayment.V1;
using SharedKernel.Events;
using Wolverine;

namespace Billings.Application.Billing.EventHandlers.IntegrationEvents;

/// <summary>Consumes idempotent cancellation requests from the order lifecycle.</summary>
public static class PaymentCancellationRequestedHandler
{
    /// <summary>Invokes cancellation for the requested order.</summary>
    /// <param name="evt">The cancellation request event.</param>
    /// <param name="bus">The message bus used to invoke the cancellation command.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after cancellation is handled.</returns>
    public static async Task Handle(PaymentCancellationRequestedIntegrationEvent evt, IMessageBus bus, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        await bus.InvokeAsync(new CancelPaymentCommand(evt.OrderId, evt.RequestId), ct).ConfigureAwait(false);
    }
}

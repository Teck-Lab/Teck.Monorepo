using Billings.Application.Billing.Payments.Features.RetryPayment.V1;
using Billings.Application.Billing.Payments.Responses;
using ErrorOr;
using SharedKernel.Events;
using Wolverine;

namespace Billings.Application.Billing.EventHandlers.IntegrationEvents;

/// <summary>Consumes shopper-authorized payment retry requests.</summary>
public static class PaymentRetryRequestedHandler
{
    /// <summary>Invokes the idempotent retry command.</summary>
    /// <param name="evt">The retry request event.</param>
    /// <param name="bus">The message bus used to invoke the retry command.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the retry command is handled.</returns>
    public static async Task Handle(PaymentRetryRequestedIntegrationEvent evt, IMessageBus bus, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var command = new RetryPaymentCommand(evt.OrderId, evt.AuthorizedAmount, evt.Currency, evt.PaymentMethodToken, evt.RequestId, evt.SourceCorrelationId);
        await bus.InvokeAsync<ErrorOr<PaymentDto>>(command, ct).ConfigureAwait(false);
    }
}

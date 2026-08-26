using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Application.Billing.Payments.Responses;
using Microsoft.Extensions.Options;
using SharedKernel.Events;
using Wolverine;

namespace Billings.Application.Billing.EventHandlers.IntegrationEvents;

/// <summary>Initiates payment capture in response to an order being placed.</summary>
public static class OrderPlacedHandler
{
    /// <summary>
    /// Consumes <see cref="OrderPlacedIntegrationEvent"/> by mapping it to a
    /// <see cref="CapturePaymentCommand"/> and invoking it in-process. All capture logic —
    /// idempotency by order id, the provider call, invoice issuance, and event publication — lives
    /// in <see cref="CapturePaymentHandler"/>, so this consumer inherits it unchanged, including
    /// idempotent re-delivery (a duplicate <c>OrderPlaced</c> for the same order returns the
    /// existing payment instead of re-charging it).
    /// </summary>
    /// <param name="evt">The order-placed event.</param>
    /// <param name="bus">The message bus used to invoke the capture command.</param>
    /// <param name="options">
    /// The payment provider options, supplying the default currency. <see cref="OrderPlacedIntegrationEvent"/>
    /// carries no currency field, so billing applies <see cref="PaymentProviderOptions.DefaultCurrency"/>
    /// until the contract adds one.
    /// </param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the capture command is handled.</returns>
    public static async Task Handle(
        OrderPlacedIntegrationEvent evt,
        IMessageBus bus,
        IOptions<PaymentProviderOptions> options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var command = new CapturePaymentCommand(evt.OrderId, evt.CustomerId, evt.Total, options.Value.DefaultCurrency);
        await bus.InvokeAsync<PaymentDto>(command, ct).ConfigureAwait(false);
    }
}

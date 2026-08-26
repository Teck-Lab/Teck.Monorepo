using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Application.Billing.Payments.Responses;
using ErrorOr;
using SharedKernel.Events;
using Wolverine;

namespace Billings.Application.Billing.EventHandlers.IntegrationEvents;

/// <summary>Starts a provider-neutral payment from the version-two checkout lifecycle.</summary>
public static class OrderPlacedV2Handler
{
    /// <summary>Converts platform-priced V2 order data into the internal capture command.</summary>
    /// <param name="evt">The version-two order event.</param>
    /// <param name="bus">The message bus used to invoke the capture command.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the capture command is handled.</returns>
    public static async Task Handle(OrderPlacedV2IntegrationEvent evt, IMessageBus bus, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var command = new LifecycleCapturePaymentCommand(evt.OrderId, evt.CustomerId ?? Guid.Empty, evt.Amount, evt.Currency)
        {
            AuthorizedAmount = evt.AuthorizedAmount,
            PaymentMethodToken = evt.PaymentMethodToken,
            RequestId = evt.RequestId,
            SourceCorrelationId = evt.SourceCorrelationId,
        };
        await bus.InvokeAsync<ErrorOr<PaymentDto>>(command, ct).ConfigureAwait(false);
    }
}

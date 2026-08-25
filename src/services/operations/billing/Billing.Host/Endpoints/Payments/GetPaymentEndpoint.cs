using Billings.Application.Billing.Payments.Features.GetPayment.V1;
using Billings.Application.Billing.Payments.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Payments;

/// <summary>Fetches a payment by identifier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetPaymentEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetPaymentRequest, PaymentDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("billing", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetPaymentRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<PaymentDto>(new GetPaymentQuery(request.PaymentId), ct);
        if (result is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/payments/{paymentId}");
        Version(0);
    }
}

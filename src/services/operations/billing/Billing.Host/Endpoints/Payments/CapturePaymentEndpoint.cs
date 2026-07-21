using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Application.Billing.Payments.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Payments;

/// <summary>Captures payment for an order.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CapturePaymentEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CapturePaymentRequest, PaymentDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("billing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CapturePaymentRequest request, CancellationToken ct)
    {
        var command = new CapturePaymentCommand(request.OrderId, request.CustomerId, request.Amount, request.Currency);
        var result = await bus.InvokeAsync<PaymentDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/payments/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/payments");
        Version(0);
    }
}

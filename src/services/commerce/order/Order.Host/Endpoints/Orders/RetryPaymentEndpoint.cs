using FastEndpoints;
using Orders.Application.Orders.Features.RetryPayment.V1;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Orders.Host.Endpoints.Orders;

/// <summary>Retries an order payment only for the persisted subject owner.</summary>
/// <param name="bus">The Wolverine message bus.</param>
public sealed class RetryPaymentEndpoint(IMessageBus bus) : AuthenticatedEndpoint<RetryPaymentRequest, EmptyResponse>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("order", "retry-payment", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(RetryPaymentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await bus.InvokeAsync<RetryPaymentInvocationResult>(new RetryPaymentInvocationCommand(request.OrderId, request.RequestId, request.PaymentMethodToken), ct);
            await SendStatusAsync(result.Outcome switch
            {
                RetryPaymentInvocationOutcome.Accepted => StatusCodes.Status202Accepted,
                RetryPaymentInvocationOutcome.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest,
            });
        }
        catch (UnauthorizedAccessException)
        {
            await Send.ForbiddenAsync(ct);
        }
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/orders/{orderId}/payment-retry");
        Version(0);
    }

    private async Task SendStatusAsync(int statusCode)
    {
        HttpContext.Response.StatusCode = statusCode;
        await HttpContext.Response.StartAsync();
    }
}

using Billings.Application.Billing.Payments.Features.ListPayments.V1;
using Billings.Application.Billing.Payments.Responses;
using FastEndpoints;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Payments;

/// <summary>Lists all payments for the tenant.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ListPaymentsEndpoint(IMessageBus bus) : AuthenticatedEndpoint<EmptyRequest, IReadOnlyList<PaymentDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("billing", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<PaymentDto>>(new ListPaymentsQuery(), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/payments");
        Version(0);
    }
}

using Billings.Application.Billing.Invoices.Features.GetInvoice.V1;
using Billings.Application.Billing.Invoices.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Invoices;

/// <summary>Fetches an invoice by identifier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetInvoiceEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetInvoiceRequest, InvoiceDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("billing", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetInvoiceRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<InvoiceDto>(new GetInvoiceQuery(request.InvoiceId), ct);
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
        Get("/invoices/{invoiceId}");
        Version(0);
    }
}

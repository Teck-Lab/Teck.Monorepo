using System.Security.Claims;
using Billings.Application.Billing.Purchases;
using Billings.Domain.Entities;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Privileged, audited confirmation of an external invoice.</summary>
/// <param name="bus">The Wolverine message bus.</param>
public sealed class ConfirmExternalInvoiceEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<ConfirmExternalInvoiceRequest, TenantPurchaseOrderDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("billing", "purchase", "internal");

    /// <inheritdoc/>
    public override async Task HandleAsync(ConfirmExternalInvoiceRequest request, CancellationToken ct)
    {
        var orderId = Route<Guid>("orderId");
        var verifier = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(verifier) || string.IsNullOrWhiteSpace(request.InvoiceReference))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        try
        {
            var order = await bus.InvokeAsync<TenantPurchaseOrder?>(
                new ConfirmExternalInvoiceCommand(orderId, request.InvoiceReference, verifier), ct).ConfigureAwait(false);
            if (order is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var dto = TenantPurchaseOrderMapping.ToDto(order);
            await Send.OkAsync(dto, ct);
        }
        catch (InvalidOperationException)
        {
            await Send.StatusCodeAsync(StatusCodes.Status409Conflict, ct);
        }
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/purchases/{orderId}/confirm-invoice");
        Version(0);
    }
}

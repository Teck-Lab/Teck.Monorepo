using System.Security.Claims;
using Billings.Application.Billing.Purchases;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Returns the authenticated tenant's current purchase state.</summary>
/// <param name="bus">The Wolverine message bus.</param>
public sealed class GetTenantPurchaseOrderEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetTenantPurchaseOrderRequest, TenantPurchaseOrderDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.ForPolicy("PendingPurchaseBuyer", "billing-api");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetTenantPurchaseOrderRequest request, CancellationToken ct)
    {
        var subject = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (request.ReservationId == Guid.Empty || string.IsNullOrWhiteSpace(subject))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var result = await bus.InvokeAsync<TenantPurchaseOrderDto?>(
            new GetTenantPurchaseOrderQuery(request.ReservationId, subject), ct).ConfigureAwait(false);
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
        Get("/purchases/{reservationId:guid}");
        Version(0);
    }
}

using System.Security.Claims;
using Billings.Application.Billing.Purchases;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SharedKernel.Infrastructure.OpenApi;
using Wolverine;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Returns the authenticated tenant's current purchase state.</summary>
/// <param name="bus">The Wolverine message bus.</param>
public sealed class GetTenantPurchaseOrderEndpoint(IMessageBus bus)
    : Endpoint<GetTenantPurchaseOrderRequest, TenantPurchaseOrderDto>
{
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
    public override void Configure()
    {
        Get("/purchases/{reservationId:guid}");
        Version(0);
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Policies("PendingPurchaseBuyer");
        Metadata(new OpenApiAudienceMetadata("billing-api"));
    }
}

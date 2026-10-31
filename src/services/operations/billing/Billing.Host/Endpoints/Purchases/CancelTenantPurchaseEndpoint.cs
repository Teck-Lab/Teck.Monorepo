using System.Security.Claims;
using Billings.Application.Billing.Purchases;
using FastEndpoints;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Cancels an active paid subscription only for its signed owner.</summary>
/// <param name="bus">The transactional Billing command dispatcher.</param>
public sealed class CancelTenantPurchaseEndpoint(IMessageBus bus) : AuthenticatedEndpoint<EmptyRequest, TenantPurchaseOrderDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.ForPolicy("PendingPurchaseBuyer", "billing-api");

    /// <inheritdoc />
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var orderId = Route<Guid>("orderId");
        var owner = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(owner))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        try
        {
            var order = await bus.InvokeAsync<TenantPurchaseOrderDto?>(
                new CancelTenantPurchaseCommand(orderId, owner), ct).ConfigureAwait(false);
            if (order is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            HttpContext.Response.Headers.CacheControl = "no-store";
            await Send.OkAsync(order, ct);
        }
        catch (InvalidOperationException)
        {
            await Send.StatusCodeAsync(StatusCodes.Status409Conflict, ct);
        }
    }

    /// <inheritdoc />
    protected override void ConfigureEndpoint()
    {
        Post("/purchases/{orderId:guid}/cancel");
        Version(0);
    }
}

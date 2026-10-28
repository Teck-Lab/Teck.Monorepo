using System.Security.Claims;
using Billings.Application.Billing.Purchases;
using Billings.Domain.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SharedKernel.Infrastructure.OpenApi;
using Wolverine;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Cancels an active paid subscription only for its signed owner.</summary>
/// <param name="bus">The transactional Billing command dispatcher.</param>
public sealed class CancelTenantPurchaseEndpoint(IMessageBus bus) : EndpointWithoutRequest<TenantPurchaseOrderDto>
{
    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken ct)
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
            var order = await bus.InvokeAsync<TenantPurchaseOrder?>(
                new CancelTenantPurchaseCommand(orderId, owner), ct).ConfigureAwait(false);
            if (order is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            HttpContext.Response.Headers.CacheControl = "no-store";
            await Send.OkAsync(TenantPurchaseOrderMapping.ToDto(order), ct);
        }
        catch (InvalidOperationException)
        {
            await Send.StatusCodeAsync(StatusCodes.Status409Conflict, ct);
        }
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Post("/purchases/{orderId:guid}/cancel");
        Version(0);
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Policies("PendingPurchaseBuyer");
        Metadata(new OpenApiAudienceMetadata("billing-api"));
    }
}

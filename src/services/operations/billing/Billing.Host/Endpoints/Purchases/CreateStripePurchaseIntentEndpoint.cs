using System.Security.Claims;
using Billings.Application.Billing.Purchases;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SharedKernel.Infrastructure.OpenApi;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Starts payment only for the signed owner of a pending global purchase.</summary>
/// <param name="handler">The Billing-owned Stripe intent orchestrator.</param>
public sealed class CreateStripePurchaseIntentEndpoint(CreateStripePurchaseIntentHandler handler)
    : EndpointWithoutRequest<StripePurchaseIntentResponse>
{
    /// <inheritdoc/>
    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderId = Route<Guid>("orderId");
        var subject = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var intent = await handler.HandleAsync(orderId, subject ?? string.Empty, ct).ConfigureAwait(false);
        if (intent is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.OkAsync(new StripePurchaseIntentResponse(orderId, intent.PaymentIntentId, intent.ClientSecret), ct);
    }

    /// <inheritdoc/>
    public override void Configure()
    {
        Post("/purchases/{orderId:guid}/payment-intent");
        Version(0);
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Policies("PendingPurchaseBuyer");
        Metadata(new OpenApiAudienceMetadata("billing-api"));
    }
}

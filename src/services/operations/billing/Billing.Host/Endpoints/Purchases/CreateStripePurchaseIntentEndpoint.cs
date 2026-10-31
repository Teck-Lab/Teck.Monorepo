using System.Data;
using System.Security.Claims;
using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Endpoints;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Starts payment only for the signed owner of a pending global purchase.</summary>
/// <param name="handler">The Billing-owned Stripe intent orchestrator.</param>
/// <param name="db">The write context holding the serialization transaction.</param>
public sealed class CreateStripePurchaseIntentEndpoint(CreateStripePurchaseIntentHandler handler, BillingDbContext db)
    : AuthenticatedEndpoint<EmptyRequest, StripePurchaseIntentResponse>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.ForPolicy("PendingPurchaseBuyer", "billing-api");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var orderId = Route<Guid>("orderId");
        var subject = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await using (var reservation = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false))
        {
            var status = await handler.ReserveAsync(orderId, subject ?? string.Empty, ct).ConfigureAwait(false);
            if (status == StripeIntentReservationStatus.NotFound)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (status == StripeIntentReservationStatus.Conflict)
            {
                await Send.StatusCodeAsync(StatusCodes.Status409Conflict, ct);
                return;
            }

            await reservation.CommitAsync(ct).ConfigureAwait(false);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        var intent = await handler.HandleAsync(orderId, subject ?? string.Empty, ct).ConfigureAwait(false);
        if (intent is null)
        {
            await Send.StatusCodeAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.OkAsync(new StripePurchaseIntentResponse(orderId, intent.PaymentIntentId, intent.ClientSecret), ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/purchases/{orderId:guid}/payment-intent");
        Version(0);
    }
}

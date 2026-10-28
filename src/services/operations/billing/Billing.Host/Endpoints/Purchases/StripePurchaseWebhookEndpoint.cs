using Billings.Application.Billing.Purchases;
using FastEndpoints;
using SharedKernel.Infrastructure.OpenApi;
using Stripe;
using Wolverine;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Accepts only Stripe-signed success events and independently verifies live captured funds.</summary>
/// <param name="bus">The Wolverine transactional command bus.</param>
/// <param name="configuration">The runtime-only Stripe webhook signing secret.</param>
public sealed class StripePurchaseWebhookEndpoint(IMessageBus bus, IConfiguration configuration)
    : EndpointWithoutRequest
{
    /// <inheritdoc/>
    public override async Task HandleAsync(CancellationToken ct)
    {
        var secret = configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            await Send.ResponseAsync(new { error = "stripe_not_configured" }, StatusCodes.Status503ServiceUnavailable, ct);
            return;
        }

        using var reader = new StreamReader(HttpContext.Request.Body);
        string body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        Event parsed;
        try
        {
            parsed = EventUtility.ConstructEvent(body, HttpContext.Request.Headers["Stripe-Signature"].ToString(),
                secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            await Send.ResponseAsync(new { error = "invalid_stripe_signature" }, StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (parsed.Type != "payment_intent.succeeded")
        {
            await Send.OkAsync(ct);
            return;
        }

        if (parsed.Data.Object is not PaymentIntent intent ||
            !await bus.InvokeAsync<bool>(new ConfirmStripePurchaseCommand(intent.Id), ct).ConfigureAwait(false))
        {
            await Send.ResponseAsync(new { error = "unverified_purchase_intent" }, StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.OkAsync(ct);
    }

    /// <inheritdoc/>
    public override void Configure()
    {
        Post("/stripe/webhooks");
        Version(0);
        AllowAnonymous();
        Metadata(new OpenApiAudienceMetadata("internal"));
    }
}

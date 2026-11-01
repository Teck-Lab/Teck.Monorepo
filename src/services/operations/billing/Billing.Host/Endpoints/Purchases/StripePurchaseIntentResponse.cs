namespace Billings.Host.Endpoints.Purchases;

/// <summary>Only the bound Stripe intent's client secret, never card credentials.</summary>
/// <param name="OrderId">The immutable Billing purchase order.</param>
/// <param name="PaymentIntentId">The Stripe PaymentIntent ID.</param>
/// <param name="ClientSecret">The future Stripe.js confirmation secret for this intent.</param>
public sealed record StripePurchaseIntentResponse(Guid OrderId, string PaymentIntentId, string ClientSecret);

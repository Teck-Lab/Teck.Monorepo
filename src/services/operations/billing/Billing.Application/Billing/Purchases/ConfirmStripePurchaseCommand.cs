namespace Billings.Application.Billing.Purchases;

/// <summary>Requests live verification of a Stripe intent from a signature-verified webhook.</summary>
/// <param name="PaymentIntentId">Stripe-issued intent ID, never an unverified local payment status.</param>
public sealed record ConfirmStripePurchaseCommand(string PaymentIntentId);

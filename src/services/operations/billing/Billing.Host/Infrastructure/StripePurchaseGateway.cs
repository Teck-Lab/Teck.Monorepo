using Billings.Application.Billing.Purchases;
using Billings.Domain.Entities;
using Stripe;

namespace Billings.Host.Infrastructure;

/// <summary>Uses the official Stripe SDK; neither a stub outcome nor a webhook payload is capture evidence.</summary>
/// <param name="intents">The configured Stripe PaymentIntent service.</param>
public sealed class StripePurchaseGateway(PaymentIntentService intents) : IStripePurchaseGateway
{
    /// <inheritdoc/>
    public async Task<StripePurchaseIntent> CreateAsync(TenantPurchaseOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        var intent = await intents.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = order.AmountMinor,
            Currency = order.Currency.ToLowerInvariant(),
            Metadata = new Dictionary<string, string>
            {
                ["purchase_order_id"] = order.Id.ToString("D"),
                ["customer_tenant_id"] = order.TenantId.ToString("D"),
            },
        }, new RequestOptions { IdempotencyKey = $"tenant-purchase-{order.Id:D}" }, cancellationToken).ConfigureAwait(false);
        return new StripePurchaseIntent(intent.Id, intent.ClientSecret);
    }

    /// <inheritdoc/>
    public async Task<StripePurchaseIntent> GetAsync(string intentId, CancellationToken cancellationToken)
    {
        var intent = await intents.GetAsync(intentId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new StripePurchaseIntent(intent.Id, intent.ClientSecret);
    }

    /// <inheritdoc/>
    public async Task<bool> IsCapturedAsync(TenantPurchaseOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
        {
            return false;
        }

        var intent = await intents.GetAsync(order.StripePaymentIntentId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return string.Equals(intent.Id, order.StripePaymentIntentId, StringComparison.Ordinal) &&
            string.Equals(intent.Status, "succeeded", StringComparison.Ordinal) &&
            intent.Amount == order.AmountMinor && intent.AmountReceived == order.AmountMinor &&
            string.Equals(intent.Currency, order.Currency, StringComparison.OrdinalIgnoreCase) &&
            intent.Metadata is not null &&
            intent.Metadata.TryGetValue("purchase_order_id", out var orderId) &&
            string.Equals(orderId, order.Id.ToString("D"), StringComparison.Ordinal) &&
            intent.Metadata.TryGetValue("customer_tenant_id", out var tenantId) &&
            string.Equals(tenantId, order.TenantId.ToString("D"), StringComparison.Ordinal);
    }
}

using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;

namespace Billings.Application.Billing.Purchases;

/// <summary>Exposes one unconfirmed Stripe intent only to the Customer-verified reservation owner.</summary>
/// <param name="context">The global Billing purchase store.</param>
/// <param name="stripe">The real Stripe PaymentIntent boundary.</param>
public sealed class CreateStripePurchaseIntentHandler(BillingDbContext context, IStripePurchaseGateway stripe)
{
    /// <summary>Retrieves or creates the pending order's provider intent.</summary>
    /// <param name="orderId">The immutable purchase order identifier.</param>
    /// <param name="buyerSubjectId">The signed Keycloak buyer subject.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The one bound intent or null for a different buyer or non-pending order.</returns>
    public async Task<StripePurchaseIntent?> HandleAsync(Guid orderId, string buyerSubjectId, CancellationToken cancellationToken)
    {
        var order = await context.TenantPurchaseOrders.SingleOrDefaultAsync(item => item.Id == orderId,
            cancellationToken).ConfigureAwait(false);
        if (order is null || order.Status != "pending" ||
            !string.Equals(order.BeneficiarySubjectId, buyerSubjectId, StringComparison.Ordinal))
        {
            return null;
        }

        var intent = order.StripePaymentIntentId is { } existing
            ? await stripe.GetAsync(existing, cancellationToken).ConfigureAwait(false)
            : await stripe.CreateAsync(order, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(intent.ClientSecret) ||
            order.StripePaymentIntentId is not null && order.StripePaymentIntentId != intent.PaymentIntentId)
        {
            throw new InvalidOperationException("Stripe did not return the bound purchase intent.");
        }

        if (order.StripePaymentIntentId is null)
        {
            order.BindStripePaymentIntent(intent.PaymentIntentId);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return intent;
    }
}

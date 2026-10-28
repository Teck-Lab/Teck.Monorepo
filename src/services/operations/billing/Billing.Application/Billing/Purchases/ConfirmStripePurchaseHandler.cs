using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Confirms a bound purchase only after independently retrieving captured Stripe evidence.</summary>
/// <param name="context">The global Billing purchase store.</param>
/// <param name="stripe">The Stripe PaymentIntent verification boundary.</param>
/// <param name="bus">The durable subscription event publisher.</param>
/// <param name="signer">The Billing-only private key for paid event attestation.</param>
public sealed class ConfirmStripePurchaseHandler(BillingDbContext context, IStripePurchaseGateway stripe, IMessageBus bus, SubscriptionPaymentSigningKey signer)
{
    /// <summary>Processes a signature-verified Stripe PaymentIntent ID idempotently.</summary>
    /// <param name="command">The intent ID trusted only after signature verification at ingress.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>True when already confirmed or confirmed now; false for unknown, inactive, or unverified intent.</returns>
    [Transactional]
    public async Task<bool> Handle(ConfirmStripePurchaseCommand command, CancellationToken cancellationToken)
    {
        var order = await context.TenantPurchaseOrders.SingleOrDefaultAsync(item => item.StripePaymentIntentId == command.PaymentIntentId,
            cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return false;
        }

        if (order.Status == "active" && order.PaymentMethod == "captured")
        {
            return true;
        }

        if (order.Status != "pending" || !await stripe.IsCapturedAsync(order, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var previous = await PurchaseRenewalPredecessor.FindAsync(context, order, cancellationToken).ConfigureAwait(false);
        var confirmedAt = DateTimeOffset.UtcNow;
        if (previous is null)
            order.ConfirmCapturedPayment(Guid.NewGuid(), confirmedAt);
        else
            order.ConfirmRenewalPayment(previous.SubscriptionId, previous.ValidUntilUtc!.Value, confirmedAt);
        await bus.PublishAsync(TenantPurchaseEventSnapshot.From(order, signer)).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

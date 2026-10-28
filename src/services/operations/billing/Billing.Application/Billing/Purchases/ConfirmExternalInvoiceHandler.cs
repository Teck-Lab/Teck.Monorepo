using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Confirms an external invoice through Wolverine transactional EF handling.</summary>
/// <param name="context">The Billing write context enlisted by Wolverine.</param>
/// <param name="bus">The Wolverine message bus.</param>
/// <param name="signer">The Billing-only private key for paid event attestation.</param>
public sealed class ConfirmExternalInvoiceHandler(BillingDbContext context, IMessageBus bus, SubscriptionPaymentSigningKey signer)
{
    /// <summary>Confirms a pending order with an audited invoice reference.</summary>
    /// <param name="command">The authenticated invoice verification request.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The confirmed order, or null when missing.</returns>
    [Transactional]
    public async Task<Billings.Domain.Entities.TenantPurchaseOrder?> Handle(ConfirmExternalInvoiceCommand command, CancellationToken cancellationToken)
    {
        var order = await context.TenantPurchaseOrders.SingleOrDefaultAsync(item => item.Id == command.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return null;
        }

        var previous = await PurchaseRenewalPredecessor.FindAsync(context, order, cancellationToken).ConfigureAwait(false);
        order.ConfirmExternalInvoice(previous?.SubscriptionId ?? Guid.NewGuid(), command.InvoiceReference, command.VerifiedBy,
            DateTimeOffset.UtcNow, previous?.ValidUntilUtc);
        await bus.PublishAsync(TenantPurchaseEventSnapshot.From(order, signer)).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return order;
    }
}

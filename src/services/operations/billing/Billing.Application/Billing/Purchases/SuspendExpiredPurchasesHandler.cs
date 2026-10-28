using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Suspends paid subscriptions whose validity elapsed without a verified renewal.</summary>
/// <param name="context">The Billing write context.</param>
/// <param name="bus">The durable outgoing event publisher.</param>
/// <param name="signer">The Billing-only suspension signing authority.</param>
public sealed class SuspendExpiredPurchasesHandler(BillingDbContext context, IMessageBus bus, SubscriptionPaymentSigningKey signer)
{
    /// <summary>Processes an idempotent batch of expired paid orders.</summary>
    /// <param name="command">The current reconciliation cutoff.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The number of newly suspended subscriptions.</returns>
    [Transactional]
    public async Task<int> Handle(SuspendExpiredPurchasesCommand command, CancellationToken cancellationToken)
    {
        if (command.NowUtc == default)
        {
            throw new ArgumentException("A reconciliation time is required.", nameof(command));
        }

        var expired = await context.TenantPurchaseOrders
            .Where(order => order.Status == "active" && order.ValidUntilUtc <= command.NowUtc &&
                !context.TenantPurchaseOrders.Any(next => next.PreviousPurchaseOrderId == order.Id &&
                    next.Status == "active" && next.SubscriptionId == order.SubscriptionId &&
                    next.ValidUntilUtc > order.ValidUntilUtc))
            .OrderBy(order => order.ValidUntilUtc)
            .Take(64)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var order in expired)
        {
            order.SuspendForFailedRenewal(command.NowUtc);
            var suspended = new SubscriptionAccessSuspendedIntegrationEvent
            {
                TenantId = order.TenantId,
                TenantIdentifier = order.TenantIdentifier,
                SourceOrderId = order.Id,
                SubscriptionId = order.SubscriptionId,
                Reason = "renewal_failed",
                SuspendedAtUtc = order.CancelledAtUtc!.Value,
            };
            suspended.Proof = signer.Sign(suspended);
            await bus.PublishAsync(suspended).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return expired.Count;
    }
}

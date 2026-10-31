using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Suspends paid subscriptions whose validity elapsed without a verified renewal.</summary>
/// <param name="expiredOrders">Bounded tracked paid-period selection.</param>
/// <param name="unitOfWork">Billing transactional commit boundary.</param>
/// <param name="bus">The durable outgoing event publisher.</param>
/// <param name="signer">The Billing-only suspension signing authority.</param>
/// <param name="tenantLock">The tenant-wide purchase and purge serialization lock.</param>
public sealed class SuspendExpiredPurchasesHandler(IExpiredTenantPurchaseOrders expiredOrders, IUnitOfWork unitOfWork,
    IMessageBus bus, SubscriptionPaymentSigningKey signer, IPurchaseTenantLock tenantLock)
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

        var expired = await expiredOrders.FindExpiredAsync(command.NowUtc, cancellationToken).ConfigureAwait(false);
        var suspendedCount = 0;
        foreach (var candidate in expired)
        {
            if (!await tenantLock.AcquireAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var current = await expiredOrders.FindExpiredAsync(command.NowUtc, cancellationToken).ConfigureAwait(false);
            var order = current.SingleOrDefault(item => item.Id == candidate.Id);
            if (order is null || order.TenantId != candidate.TenantId)
            {
                continue;
            }

            order.SuspendForFailedRenewal(command.NowUtc);
            var suspended = new SubscriptionAccessSuspendedIntegrationEvent
            {
                TenantId = order.TenantId,
                TenantIdentifier = order.TenantIdentifier,
                SourceOrderId = order.Id,
                SubscriptionId = order.SubscriptionId,
                SuspendedAtUtc = order.CancelledAtUtc!.Value,
                Reason = "renewal_failed",
            };
            suspended.Proof = signer.Sign(suspended);
            await bus.PublishAsync(suspended).ConfigureAwait(false);
            suspendedCount++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return suspendedCount;
    }
}

using SharedKernel.Events;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Admits Billing-attested suspension only for the matching tenant and paid source order.</summary>
public sealed class SignedTenantDatabaseSuspensionIntake(ITenantDatabaseRequestStore store, SubscriptionPaymentVerificationKeys billingKeys)
{
    /// <summary>Queues revocation for the current period; a stale prior-period suspension has no effect.</summary>
    public async Task Handle(SubscriptionAccessSuspendedIntegrationEvent suspension, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(suspension);
        if (!billingKeys.Verify(suspension) || suspension.TenantId == Guid.Empty ||
            suspension.SourceOrderId == Guid.Empty || suspension.SubscriptionId == Guid.Empty ||
            suspension.SuspendedAtUtc == default || string.IsNullOrWhiteSpace(suspension.TenantIdentifier) ||
            suspension.Reason is not ("cancelled" or "renewal_failed"))
        {
            throw new InvalidOperationException("A Billing-attested tenant-specific suspension is required.");
        }

        await store.SuspendAsync(suspension.SubscriptionId, suspension.SourceOrderId, suspension.TenantId,
            suspension.TenantIdentifier, suspension.SuspendedAtUtc, ct).ConfigureAwait(false);
    }
}

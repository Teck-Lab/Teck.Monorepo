using SharedKernel.Events;
using Wolverine.Attributes;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Verifies Customer's independent two-operator proof before queuing a tenant-specific destructive decision.</summary>
[WolverineHandler]
public sealed class SignedTenantPurgeIntake(ITenantPurgeStore store, TenantPurgeVerificationKeys keys,
    TenantPurgeRetentionPolicy retention)
{
    /// <summary>Persists signed approval idempotently; the polling worker separately verifies a restored backup.</summary>
    /// <param name="approval">Customer's signed, source-order-bound decision.</param>
    /// <param name="ct">Cancellation signal.</param>
    /// <returns>Task completed after durable intake.</returns>
    public async Task Handle(TenantPurgeApprovedIntegrationEvent approval, CancellationToken ct)
    {
        if (!keys.Verify(approval) ||
            approval.EligibleAtUtc < approval.SuspendedAtUtc + retention.Window ||
            DateTimeOffset.UtcNow < approval.EligibleAtUtc)
            throw new InvalidOperationException("A signed and locally retention-eligible Customer purge decision is required.");
        await store.EnqueuePurgeApprovalAsync(approval, retention.Window, ct).ConfigureAwait(false);
    }
}

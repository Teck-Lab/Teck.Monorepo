using SharedKernel.Events;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>The current locked CNPG data owner; the Cluster UID fences replacement under the same name.</summary>
/// <param name="TenantId">Canonical registry UUID.</param>
/// <param name="Identifier">Customer slug for source-order consistency.</param>
/// <param name="SourceOrderId">Current paid period UUID.</param>
/// <param name="ClusterUid">UID of the suspended CNPG Cluster.</param>
/// <param name="RecordedRecovery">Previously audited recovery proof after an interrupted purge.</param>
public sealed record TenantPurgeTarget(Guid TenantId, string Identifier, Guid SourceOrderId, Guid ClusterUid,
    TenantBackupRecoveryEvidence? RecordedRecovery = null);

/// <summary>Proof that a physical backup of this tenant Cluster was restored and its five databases inspected.</summary>
/// <param name="BackupUid">UID of an independently retained CNPG Backup resource.</param>
/// <param name="RestoredClusterUid">UID of the disposable successfully restored CNPG Cluster.</param>
/// <param name="BackupName">Namespace-scoped backup reference retained after source Cluster deletion.</param>
/// <param name="VerifiedAtUtc">Time of read-only recovery verification.</param>
public sealed record TenantBackupRecoveryEvidence(Guid BackupUid, Guid RestoredClusterUid, string BackupName,
    DateTimeOffset VerifiedAtUtc);

/// <summary>Serializes destructive operations with paid-event intake under the current retained-order lock.</summary>
public interface ITenantPurgeStore
{
    /// <summary>Durably queues one Customer-signed approval for asynchronous recovery verification.</summary>
    Task<bool> EnqueuePurgeApprovalAsync(TenantPurgeApprovedIntegrationEvent approval,
        TimeSpan recoveryWindow, CancellationToken ct);
    /// <summary>Reads retained or partially purging approvals that still require reconciliation.</summary>
    Task<IReadOnlyList<TenantPurgeApprovedIntegrationEvent>> GetPendingPurgeApprovalsAsync(int limit, CancellationToken ct);
    /// <summary>Executes the operation only for a still-suspended, current paid order past local retention; marks completion atomically.</summary>
    Task<bool> WithCurrentRetainedRequestLockAsync(TenantPurgeApprovedIntegrationEvent approval,
        TimeSpan recoveryWindow, Func<TenantPurgeTarget, CancellationToken, Task> action, CancellationToken ct);
    /// <summary>Durably binds signed approval and restored backup evidence to the current locked paid period.</summary>
    Task RecordVerifiedRecoveryAsync(TenantPurgeApprovedIntegrationEvent approval,
        TenantBackupRecoveryEvidence evidence, CancellationToken ct);
}

/// <summary>Restores a physical backup to a disposable CNPG Cluster and inspects all five application databases.</summary>
public interface ITenantRecoverableBackupVerifier
{
    /// <summary>Returns evidence only after an independent recovery succeeds for the exact suspended Cluster UID.</summary>
    Task<TenantBackupRecoveryEvidence?> VerifyAsync(TenantPurgeTarget target, CancellationToken ct);
}

/// <summary>Deletes only the fenced tenant CNPG Cluster and its five Database CRs after restore verification.</summary>
public interface ITenantCnpgPurger
{
    /// <summary>Performs guarded deletion and verifies all tenant-owned resources are absent.</summary>
    Task PurgeAsync(TenantPurgeTarget target, TenantBackupRecoveryEvidence evidence, CancellationToken ct);
}

/// <summary>Durable authorization held by Billing until destructive tenant work is fully complete.</summary>
public interface IBillingPurgeFenceClient
{
    /// <summary>Acquires or resumes the approval's durable gate, or returns null when Billing denies it.</summary>
    Task<Guid?> TryAcquireAsync(Guid tenantId, Guid subscriptionId, Guid sourceOrderId, Guid approvalId, CancellationToken ct);
    /// <summary>Marks a successfully completed destructive operation as released from its permanent gate.</summary>
    Task<bool> CompleteAsync(Guid grantId, CancellationToken ct);
}

/// <summary>Authorizes destructive work only for a current signed Customer decision and verified recoverable backup.</summary>
/// <param name="store">Tenant-scoped source-order lock and durable purge status.</param>
/// <param name="keys">Independent trusted Customer approval public keys.</param>
/// <param name="recovery">Cluster-backed independent physical recovery verifier.</param>
/// <param name="purger">Fenced Kubernetes resource deletion.</param>
/// <param name="retention">Worker's independent positive recovery policy.</param>
/// <param name="billingFence">Billing-authoritative durable gate held throughout destructive work.</param>
public sealed class TenantPurgeCoordinator(ITenantPurgeStore store, TenantPurgeVerificationKeys keys,
    ITenantRecoverableBackupVerifier recovery, ITenantCnpgPurger purger, TenantPurgeRetentionPolicy retention,
    IBillingPurgeFenceClient billingFence)
{
    /// <summary>Rejects stale, wrong-tenant, premature or unbacked decisions before any deletion.</summary>
    /// <param name="approval">Customer's independently signed two-person decision.</param>
    /// <param name="ct">Cancellation signal.</param>
    /// <returns>Task completed after idempotent safe purge.</returns>
    public async Task PurgeAsync(TenantPurgeApprovedIntegrationEvent approval, CancellationToken ct)
    {
        if (!keys.Verify(approval) ||
            approval.EligibleAtUtc < approval.SuspendedAtUtc + retention.Window ||
            approval.ApprovedAtUtc < approval.EligibleAtUtc ||
            DateTimeOffset.UtcNow < approval.EligibleAtUtc)
        {
            throw new InvalidOperationException("An authenticated, retention-eligible Customer purge approval is required.");
        }
        await store.WithCurrentRetainedRequestLockAsync(approval, retention.Window, async (target, lockedCt) =>
        {
            var grantId = await billingFence.TryAcquireAsync(approval.TenantId, approval.SubscriptionId,
                approval.SourceOrderId, approval.ApprovalId, lockedCt).ConfigureAwait(false);
            if (grantId is null)
            {
                throw new InvalidOperationException("Billing did not authorize the destructive tenant purge.");
            }

            if (target.TenantId != approval.TenantId || target.SourceOrderId != approval.SourceOrderId ||
                !string.Equals(target.Identifier, approval.TenantIdentifier, StringComparison.Ordinal) ||
                target.ClusterUid == Guid.Empty)
            {
                throw new InvalidOperationException("The approved tenant and current suspended CNPG Cluster do not match.");
            }

            TenantBackupRecoveryEvidence? evidence = await recovery.VerifyAsync(target, lockedCt).ConfigureAwait(false);
            if (evidence is null || evidence.BackupUid == Guid.Empty || evidence.RestoredClusterUid == Guid.Empty ||
                string.IsNullOrWhiteSpace(evidence.BackupName) || evidence.VerifiedAtUtc < approval.SuspendedAtUtc)
            {
                throw new InvalidOperationException("An independently restored tenant backup is required before purge.");
            }

            await store.RecordVerifiedRecoveryAsync(approval, evidence, lockedCt).ConfigureAwait(false);
            await purger.PurgeAsync(target, evidence, lockedCt).ConfigureAwait(false);
            if (!await billingFence.CompleteAsync(grantId.Value, lockedCt).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Billing did not confirm completion of the tenant purge gate.");
            }
        }, ct).ConfigureAwait(false);
    }
}

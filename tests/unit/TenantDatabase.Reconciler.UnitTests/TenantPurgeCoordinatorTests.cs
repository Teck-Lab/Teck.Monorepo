using System.Security.Cryptography;
using SharedKernel.Events;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Teck.TenantDatabase.Reconciler.UnitTests;

public sealed class TenantPurgeCoordinatorTests
{
    [Fact]
    public async Task PurgeAsync_RequiresBillingGateCompletionAfterDeletion()
    {
        using RSA signer = RSA.Create(2048);
        using var keys = new TenantPurgeVerificationKeys([signer.ExportSubjectPublicKeyInfoPem()]);
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var suspendedAt = DateTimeOffset.UtcNow.AddDays(-32);
        var approval = CreateSignedApproval(signer, tenantId, orderId, subscriptionId, suspendedAt);
        var fence = new FakeBillingPurgeFence { CompletionDenied = true };
        var store = new FakePurgeStore(tenantId, orderId, subscriptionId, suspendedAt);
        var purger = new FakePurger(() => Assert.Equal(0, fence.Completions));
        var coordinator = new TenantPurgeCoordinator(store, keys,
            new FakeRecovery { Verified = true }, purger, new TenantPurgeRetentionPolicy(TimeSpan.FromDays(30)), fence);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PurgeAsync(approval, CancellationToken.None));
        Assert.Equal(1, purger.Calls);
        Assert.Equal(1, fence.Completions);
        Assert.Equal(0, store.Completed);
    }

    [Fact]
    public async Task PurgeAsync_RequiresCurrentSuspendedPeriodElapsedRetentionAndVerifiedRecovery()
    {
        using RSA signer = RSA.Create(2048);
        using var keys = new TenantPurgeVerificationKeys([signer.ExportSubjectPublicKeyInfoPem()]);
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var suspendedAt = DateTimeOffset.UtcNow.AddDays(-32);
        var approval = CreateSignedApproval(signer, tenantId, orderId, subscriptionId, suspendedAt);
        var store = new FakePurgeStore(tenantId, orderId, subscriptionId, suspendedAt);
        var recovery = new FakeRecovery();
        var deleted = new FakePurger();
        var billingFence = new FakeBillingPurgeFence();
        var coordinator = new TenantPurgeCoordinator(store, keys, recovery, deleted,
            new TenantPurgeRetentionPolicy(TimeSpan.FromDays(30)), billingFence);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PurgeAsync(
            CreateSignedApproval(signer, Guid.NewGuid(), orderId, subscriptionId, suspendedAt), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PurgeAsync(
            CreateSignedApproval(signer, tenantId, Guid.NewGuid(), subscriptionId, suspendedAt), CancellationToken.None));
        Assert.Equal(0, billingFence.Acquisitions);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PurgeAsync(approval, CancellationToken.None));
        Assert.Equal(1, recovery.Calls);
        Assert.Equal(1, billingFence.Acquisitions);
        Assert.Equal(0, billingFence.Completions);
        recovery.Verified = true;
        await coordinator.PurgeAsync(approval, CancellationToken.None);
        Assert.Equal(1, billingFence.Completions);
        Assert.Equal(1, store.Completed);
        await coordinator.PurgeAsync(approval, CancellationToken.None);
        Assert.Equal(1, deleted.Calls);
    }

    [Fact]
    public async Task PurgeAsync_RejectsForgedOrRenewedApprovalBeforeBackupProbe()
    {
        using RSA signer = RSA.Create(2048);
        using var keys = new TenantPurgeVerificationKeys([signer.ExportSubjectPublicKeyInfoPem()]);
        var tenantId = Guid.NewGuid();
        var oldOrder = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var suspendedAt = DateTimeOffset.UtcNow.AddDays(-32);
        var approval = CreateSignedApproval(signer, tenantId, oldOrder, subscriptionId, suspendedAt);
        approval.SourceOrderId = Guid.NewGuid();
        var backup = new FakeRecovery { Verified = true };
        var deleted = new FakePurger();
        var gate = new FakeBillingPurgeFence();
        var coordinator = new TenantPurgeCoordinator(new FakePurgeStore(tenantId, oldOrder, subscriptionId, suspendedAt), keys,
            backup, deleted, new TenantPurgeRetentionPolicy(TimeSpan.FromDays(30)), gate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PurgeAsync(approval, CancellationToken.None));
        Assert.Equal(0, backup.Calls);
        Assert.Equal(0, deleted.Calls);

        approval = CreateSignedApproval(signer, tenantId, oldOrder, subscriptionId, suspendedAt);
        var renewed = new TenantPurgeCoordinator(new FakePurgeStore(tenantId, Guid.NewGuid(), subscriptionId, suspendedAt), keys,
            backup, deleted, new TenantPurgeRetentionPolicy(TimeSpan.FromDays(30)), new FakeBillingPurgeFence());
        await Assert.ThrowsAsync<InvalidOperationException>(() => renewed.PurgeAsync(approval, CancellationToken.None));
        Assert.Equal(0, backup.Calls);
        Assert.Equal(0, deleted.Calls);
    }

    private static TenantPurgeApprovedIntegrationEvent CreateSignedApproval(RSA signer, Guid tenantId, Guid orderId,
        Guid subscriptionId, DateTimeOffset suspendedAt)
    {
        var approval = new TenantPurgeApprovedIntegrationEvent
        {
            ApprovalId = Guid.NewGuid(), TenantId = tenantId, TenantIdentifier = $"tenant-{tenantId:N}",
            SourceOrderId = orderId, SubscriptionId = subscriptionId, SuspendedAtUtc = suspendedAt,
            EligibleAtUtc = suspendedAt.AddDays(30), ApprovedAtUtc = DateTimeOffset.UtcNow,
            FirstApproverSubjectId = "manager-one", SecondApproverSubjectId = "manager-two",
        };
        approval.Proof = TenantPurgeApprovalProof.Sign(approval, signer);
        return approval;
    }

    [Fact]
    public async Task PurgeAsync_BillingDenialStopsBeforeBackupAndKeepsDeletionDisabled()
    {
        using RSA signer = RSA.Create(2048);
        using var keys = new TenantPurgeVerificationKeys([signer.ExportSubjectPublicKeyInfoPem()]);
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var suspendedAt = DateTimeOffset.UtcNow.AddDays(-32);
        var approval = CreateSignedApproval(signer, tenantId, orderId, subscriptionId, suspendedAt);
        var gate = new FakeBillingPurgeFence { Denied = true };
        var recovery = new FakeRecovery { Verified = true };
        var deleted = new FakePurger();
        var coordinator = new TenantPurgeCoordinator(new FakePurgeStore(tenantId, orderId, subscriptionId, suspendedAt),
            keys, recovery, deleted, new TenantPurgeRetentionPolicy(TimeSpan.FromDays(30)), gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PurgeAsync(approval, CancellationToken.None));

        Assert.Equal(0, recovery.Calls);
        Assert.Equal(0, deleted.Calls);
        Assert.Equal(0, gate.Completions);
    }

    private sealed class FakePurgeStore(Guid tenantId, Guid orderId, Guid subscriptionId, DateTimeOffset suspendedAt)
        : ITenantPurgeStore
    {
        public int Completed { get; private set; }
        public int EvidenceRecorded { get; private set; }
        private TenantPurgeApprovedIntegrationEvent? pending;
        public Task<bool> EnqueuePurgeApprovalAsync(TenantPurgeApprovedIntegrationEvent approval,
            TimeSpan recoveryWindow, CancellationToken ct)
        {
            if (approval.TenantId != tenantId || approval.SourceOrderId != orderId ||
                approval.SubscriptionId != subscriptionId || approval.SuspendedAtUtc != suspendedAt ||
                DateTimeOffset.UtcNow < suspendedAt + recoveryWindow)
                throw new InvalidOperationException("Purge approval is not for the retained paid period.");
            if (pending is not null && (pending.ApprovalId != approval.ApprovalId || pending.Proof != approval.Proof))
                throw new InvalidOperationException("Purge decision cannot change.");
            bool added = pending is null;
            pending = approval;
            return Task.FromResult(added);
        }
        public Task<IReadOnlyList<TenantPurgeApprovedIntegrationEvent>> GetPendingPurgeApprovalsAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenantPurgeApprovedIntegrationEvent>>(pending is not null && Completed == 0 ? [pending] : []);
        public Task RecordVerifiedRecoveryAsync(TenantPurgeApprovedIntegrationEvent approval,
            TenantBackupRecoveryEvidence evidence, CancellationToken ct)
        {
            Assert.Equal(orderId, approval.SourceOrderId);
            Assert.NotEqual(Guid.Empty, evidence.BackupUid);
            EvidenceRecorded++;
            return Task.CompletedTask;
        }
        public async Task<bool> WithCurrentRetainedRequestLockAsync(TenantPurgeApprovedIntegrationEvent approval,
            TimeSpan recoveryWindow, Func<TenantPurgeTarget, CancellationToken, Task> action, CancellationToken ct)
        {
            if (approval.TenantId != tenantId || approval.SourceOrderId != orderId ||
                approval.SubscriptionId != subscriptionId || approval.SuspendedAtUtc != suspendedAt ||
                DateTimeOffset.UtcNow < suspendedAt + recoveryWindow)
                throw new InvalidOperationException("Approval is not current and eligible.");
            if (Completed > 0) return false;
            await action(new TenantPurgeTarget(tenantId, approval.TenantIdentifier, orderId, Guid.NewGuid()), ct);
            Completed++;
            return true;
        }
    }

    private sealed class FakeRecovery : ITenantRecoverableBackupVerifier
    {
        public bool Verified { get; set; }
        public int Calls { get; private set; }
        public Task<TenantBackupRecoveryEvidence?> VerifyAsync(TenantPurgeTarget target, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Verified
                ? new TenantBackupRecoveryEvidence(Guid.NewGuid(), Guid.NewGuid(), $"backup-{target.TenantId:N}", DateTimeOffset.UtcNow)
                : null);
        }
    }

    private sealed class FakePurger(Action? onPurge = null) : ITenantCnpgPurger
    {
        public int Calls { get; private set; }
        public Task PurgeAsync(TenantPurgeTarget target, TenantBackupRecoveryEvidence evidence, CancellationToken ct)
        {
            Calls++;
            onPurge?.Invoke();
            return Task.CompletedTask;
        }
    }
    private sealed class FakeBillingPurgeFence : IBillingPurgeFenceClient
    {
        public bool Denied { get; init; }
        public bool CompletionDenied { get; set; }
        public int Acquisitions { get; private set; }
        public int Completions { get; private set; }
        private readonly Guid grantId = Guid.NewGuid();
        public Task<Guid?> TryAcquireAsync(Guid tenantId, Guid subscriptionId, Guid sourceOrderId, Guid approvalId, CancellationToken ct)
        {
            Acquisitions++;
            return Task.FromResult<Guid?>(Denied ? null : grantId);
        }
        public Task<bool> CompleteAsync(Guid grantId, CancellationToken ct)
        {
            Completions++;
            return Task.FromResult(!CompletionDenied);
        }
    }
}

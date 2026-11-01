using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using SharedKernel.Events;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Teck.TenantDatabase.Reconciler.UnitTests;


public sealed class TenantDatabaseReconcilerTests
{
    [Fact]
    public async Task ReconcileAsync_WhenRetentionElapsed_RetainsResourcesWithoutApprovedDeletion()
    {
        var backend = new FakeBackend();
        var sut = new TenantDatabaseReconciler(backend, new FakeMigrations(), NullLogger<TenantDatabaseReconciler>.Instance);

        TenantDatabaseStatus result = await sut.ReconcileAsync(DesiredTenant("dedicated", TenantSubscriptionStatus.Suspended) with { SuspendedAt = DateTimeOffset.UtcNow.AddHours(-3) }, CancellationToken.None);

        Assert.Equal(TenantDatabasePhase.Retained, result.Phase);
        Assert.Empty(backend.Clusters);
    }

    [Fact]
    public async Task Signed_paid_snapshot_only_creates_authoritative_dedicated_database_request()
    {
        using var privateKey = System.Security.Cryptography.RSA.Create(2048);
        using var keys = new SharedKernel.Events.SubscriptionPaymentVerificationKeys([privateKey.ExportSubjectPublicKeyInfoPem()]);
        var store = new FakeRequestStore();
        var intake = new SignedTenantDatabaseIntake(store, keys);
        var paid = new SharedKernel.Events.SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TenantIdentifier = "acme", PaymentMethod = "captured", DatabaseStrategy = "dedicated",
            DatabaseProvider = "postgres", HasReadReplicas = true, ReplicaCount = 2,
            HasScheduledBackups = true, HasHighAvailability = true,
            PlanId = "Business", PlanVersion = "2026-10", Currency = "USD", AmountMinor = 10000,
            ValidFromUtc = DateTimeOffset.UtcNow, ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(30),
            ConfirmedAtUtc = DateTimeOffset.UtcNow,
            PolicyVersion = 4, EffectiveGracePeriodDays = 7, EffectiveBackupRetentionDays = 30,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => intake.Handle(paid, CancellationToken.None));
        Assert.Empty(store.Pending);
        paid.Proof = SharedKernel.Events.SubscriptionPaymentProof.Sign(paid, privateKey);
        await intake.Handle(paid, CancellationToken.None);
        var request = Assert.Single(store.Pending);
        Assert.Equal(paid.SubscriptionId, request.SubscriptionId);
        Assert.Equal(paid.TenantId.ToString("D"), request.DesiredState.TenantId);
        Assert.Equal(TenantSubscriptionStatus.Active, request.DesiredState.SubscriptionStatus);
        Assert.Equal(2, request.DesiredState.ReadReplicas);
        Assert.True(request.DesiredState.HasScheduledBackups);
        Assert.True(request.DesiredState.HasHighAvailability);
        Assert.Equal(paid.ValidUntilUtc, request.DesiredState.PaidUntilUtc);
        Assert.Equal(paid.EffectiveGracePeriodDays, request.DesiredState.GracePeriodDays);
        Assert.Equal(paid.EffectiveBackupRetentionDays, request.DesiredState.BackupRetentionDays);
        Assert.Equal(paid.PreviousSourceOrderId, request.PreviousSourceOrderId);
        await intake.Handle(paid, CancellationToken.None);
        Assert.Single(store.Pending);
        paid.HasHighAvailability = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => intake.Handle(paid, CancellationToken.None));

        paid.ReplicaCount = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => intake.Handle(paid, CancellationToken.None));
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenRequestIsPersisted_ReconcilesAndPersistsExternallyVisiblePhase()
    {
        var backend = new FakeBackend { SecretAvailable = true, ClusterReady = true, DatabaseReady = true };
        var store = new FakeRequestStore();
        var reconciler = new TenantDatabaseReconciler(backend, new FakeMigrations(), NullLogger<TenantDatabaseReconciler>.Instance);
        var publisher = new RecordingReadinessPublisher();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var signingKey = new DatabaseReadinessSigningKey(Convert.ToBase64String(key));
        var worker = new TenantDatabaseWorker(store, reconciler, publisher, signingKey,
            new UnexpectedFinalBackupRequester(), NullLogger<TenantDatabaseWorker>.Instance);
        TenantDatabaseRequest request = new(Guid.NewGuid(), DesiredTenant("dedicated", TenantSubscriptionStatus.Active),
            DateTimeOffset.UtcNow, Guid.NewGuid(), "signed");
        store.Pending.Add(request);

        int processed = await worker.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, processed);
        Assert.Equal(TenantDatabasePhase.Ready, store.Statuses[request.SubscriptionId].Phase);
        Assert.Equal(request.DesiredState.TenantId, store.Statuses[request.SubscriptionId].TenantId);
        Assert.True(store.Published.Contains(request.SubscriptionId));
        var ready = Assert.Single(publisher.Events);
        Assert.Equal("database", ready.Provisioner);
        Assert.Equal(request.SourceOrderId, ready.SourceOrderId);
        Assert.Equal(store.Statuses[request.SubscriptionId].EvidenceId, ready.EvidenceId);
        Assert.True(TenantReadinessProof.Verify(ready, key));
        Assert.Equal(0, await worker.ProcessBatchAsync(TestContext.Current.CancellationToken));

        var second = request with { SubscriptionId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid() };
        store.Pending.Add(second);
        publisher.FailNext = true;
        await worker.ProcessBatchAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TenantDatabasePhase.Ready, store.Statuses[second.SubscriptionId].Phase);
        Assert.False(store.Published.Contains(second.SubscriptionId));
        await worker.ProcessBatchAsync(TestContext.Current.CancellationToken);
        Assert.True(store.Published.Contains(second.SubscriptionId));
        Assert.Equal(2, publisher.Events.Count);
    }

    [Fact]
    public async Task ProcessBatchAsync_DueRetainedRenewalRequestsOrderFencedBackupWithoutReactivating()
    {
        var store = new FakeRequestStore();
        var backup = new RecordingFinalBackupRequester();
        var publisher = new RecordingReadinessPublisher();
        var uid = Guid.NewGuid();
        var request = new TenantDatabaseRequest(Guid.NewGuid(), DesiredTenant("dedicated", TenantSubscriptionStatus.Suspended) with
        {
            SuspendedAt = DateTimeOffset.UtcNow.AddDays(-8), PaidUntilUtc = DateTimeOffset.UtcNow.AddDays(-8),
            GracePeriodDays = 7,
        }, DateTimeOffset.UtcNow.AddDays(-365), Guid.NewGuid(), "signed", LastClusterUid: uid, IsDueRetainedRenewal: true);
        store.Pending.Add(request);
        var worker = new TenantDatabaseWorker(store, new TenantDatabaseReconciler(new FakeBackend(), new FakeMigrations(),
            NullLogger<TenantDatabaseReconciler>.Instance), publisher,
            new DatabaseReadinessSigningKey(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), backup,
            NullLogger<TenantDatabaseWorker>.Instance);

        Assert.Equal(1, await worker.ProcessBatchAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new(Guid.Parse(request.DesiredState.TenantId), request.SourceOrderId, uid), Assert.Single(backup.Targets));
        Assert.Empty(publisher.Events);
        Assert.Empty(store.Statuses);
    }


    [Fact]
    public async Task ProcessBatchAsync_DueRenewalWithCompletedBackupDoesNotAdvanceWithoutRestoreProof()
    {
        var store = new FakeRequestStore();
        var backup = new RecordingFinalBackupRequester
        {
            Completed = new TenantCompletedFinalBackup(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "tenant-final-order", "backup-42", "000000010000000000000042"),
        };
        var uid = Guid.NewGuid();
        var request = new TenantDatabaseRequest(Guid.NewGuid(), DesiredTenant("dedicated", TenantSubscriptionStatus.Suspended),
            DateTimeOffset.UtcNow, Guid.NewGuid(), "signed", LastClusterUid: uid, IsDueRetainedRenewal: true);
        store.Pending.Add(request);
        var worker = new TenantDatabaseWorker(store, new TenantDatabaseReconciler(new FakeBackend(), new FakeMigrations(),
            NullLogger<TenantDatabaseReconciler>.Instance), new RecordingReadinessPublisher(),
            new DatabaseReadinessSigningKey(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), backup,
            NullLogger<TenantDatabaseWorker>.Instance);

        await worker.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Empty(store.Statuses);
        Assert.Empty(store.Published);
    }



    [Fact]
    public async Task ReconcileAsync_PaidEntitlementsReachClusterProvisioning()
    {
        var backend = new FakeBackend();
        var reconciler = new TenantDatabaseReconciler(backend, new FakeMigrations(), NullLogger<TenantDatabaseReconciler>.Instance);
        var desired = DesiredTenant("dedicated", TenantSubscriptionStatus.Active) with
        {
            ReadReplicas = 2, HasScheduledBackups = true, HasHighAvailability = true,
        };

        await reconciler.ReconcileAsync(desired, TestContext.Current.CancellationToken, Guid.NewGuid());

        var cluster = Assert.Single(backend.Clusters);
        Assert.Equal(4, cluster.Instances);
        Assert.True(cluster.HasScheduledBackups);
        Assert.True(cluster.HasHighAvailability);
    }

    [Fact]
    public async Task ReconcileAsync_HaWithoutReadReplicaPurchase_ProvisionsStandby()
    {
        var backend = new FakeBackend();
        var reconciler = new TenantDatabaseReconciler(backend, new FakeMigrations(), NullLogger<TenantDatabaseReconciler>.Instance);
        var desired = DesiredTenant("dedicated", TenantSubscriptionStatus.Active) with
        {
            ReadReplicas = 0, HasHighAvailability = true,
        };

        await reconciler.ReconcileAsync(desired, TestContext.Current.CancellationToken, Guid.NewGuid());

        Assert.Equal(2, Assert.Single(backend.Clusters).Instances);
    }

    [Fact]
    public async Task ReconcileAsync_WhenDedicatedPaidTenantIsMissing_CreatesIsolatedClusterAndWaitsForCredentialsAndMigration()
    {
        var backend = new FakeBackend();
        var migrations = new FakeMigrations();
        var reconciler = new TenantDatabaseReconciler(backend, migrations, NullLogger<TenantDatabaseReconciler>.Instance);
        TenantDatabaseDesiredState desired = DesiredTenant("dedicated", TenantSubscriptionStatus.Active);

        TenantDatabaseStatus result = await reconciler.ReconcileAsync(desired, TestContext.Current.CancellationToken);

        Assert.Equal(TenantDatabasePhase.AwaitingCredentials, result.Phase);
        Assert.Equal("tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", backend.Clusters.Single().Name);
        Assert.Equal(2, backend.Clusters.Single().Instances);
        Assert.Empty(backend.Databases);
        Assert.Empty(migrations.Services);
    }

    [Fact]
    public async Task ReconcileAsync_WhenClusterIsUnready_DoesNotMigrateOrDeclareTenantReady()
    {
        var backend = new FakeBackend { SecretAvailable = true, ClusterReady = false, DatabaseReady = true };
        var migrations = new FakeMigrations();
        var reconciler = new TenantDatabaseReconciler(backend, migrations, NullLogger<TenantDatabaseReconciler>.Instance);

        TenantDatabaseStatus result = await reconciler.ReconcileAsync(
            DesiredTenant("dedicated", TenantSubscriptionStatus.Active), TestContext.Current.CancellationToken);

        Assert.Equal(TenantDatabasePhase.Provisioning, result.Phase);
        Assert.Empty(backend.Databases);
        Assert.Empty(migrations.Services);
    }

    [Fact]
    public async Task ReconcileAsync_WhenPaidBackupOrHaNotObserved_DoesNotPublishDatabaseReadiness()
    {
        var backend = new FakeBackend
        {
            SecretAvailable = true, ClusterReady = true, DatabaseReady = true, EntitlementsReady = false,
        };
        var migrations = new FakeMigrations();
        var reconciler = new TenantDatabaseReconciler(backend, migrations, NullLogger<TenantDatabaseReconciler>.Instance);
        var desired = DesiredTenant("dedicated", TenantSubscriptionStatus.Active) with
        {
            HasScheduledBackups = true, HasHighAvailability = true,
        };

        TenantDatabaseStatus result = await reconciler.ReconcileAsync(desired, TestContext.Current.CancellationToken);

        Assert.Equal(TenantDatabasePhase.Provisioning, result.Phase);
        Assert.Null(result.EvidenceId);
        Assert.Empty(migrations.Services);
        Assert.Empty(backend.Databases);
    }

    [Fact]
    public async Task ReconcileAsync_WhenCredentialsAndDatabaseAreReady_RunsMigrationBeforeReady()
    {
        var backend = new FakeBackend { SecretAvailable = true, ClusterReady = true, DatabaseReady = true };
        var migrations = new FakeMigrations();
        var reconciler = new TenantDatabaseReconciler(backend, migrations, NullLogger<TenantDatabaseReconciler>.Instance);
        TenantDatabaseDesiredState desired = DesiredTenant("dedicated", TenantSubscriptionStatus.Active);

        TenantDatabaseStatus result = await reconciler.ReconcileAsync(desired, TestContext.Current.CancellationToken);

        Assert.Equal(TenantDatabasePhase.Ready, result.Phase);
        Assert.NotNull(result.EvidenceId);
        Assert.NotEqual(Guid.Empty, result.EvidenceId.Value);
        Assert.Equal(["basket", "catalog", "inventory", "order", "pricing"], migrations.Services);
        Assert.Equal(5, backend.Databases.Count);
        Assert.All(backend.Databases, database => Assert.Equal("tenant-databases", database.Namespace));
        Assert.All(backend.Databases, database => Assert.Equal("tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", database.ClusterName));
        Assert.Equal(["basket", "catalog", "inventory", "order", "pricing"], backend.Databases
            .Select(database => database.Name[40..]).ToArray());
        Assert.Equal(2, backend.Clusters.Single().Instances);
    }

    [Theory]
    [InlineData("shared", TenantSubscriptionStatus.Active)]
    [InlineData("external", TenantSubscriptionStatus.Active)]
    [InlineData("dedicated", TenantSubscriptionStatus.Pending)]
    [InlineData("dedicated", TenantSubscriptionStatus.Cancelled)]
    public async Task ReconcileAsync_WhenStrategyOrSubscriptionDoesNotPermitDedicatedResources_DoesNotCreateResources(string strategy, TenantSubscriptionStatus status)
    {
        var backend = new FakeBackend();
        var reconciler = new TenantDatabaseReconciler(backend, new FakeMigrations(), NullLogger<TenantDatabaseReconciler>.Instance);

        TenantDatabaseStatus result = await reconciler.ReconcileAsync(DesiredTenant(strategy, status), TestContext.Current.CancellationToken);

        Assert.Equal(TenantDatabasePhase.NotManaged, result.Phase);
        Assert.Empty(backend.Clusters);
        Assert.Empty(backend.Databases);
    }

    [Fact]
    public async Task ReconcileAsync_WhenTenantIdentifierIsNotCanonicalUuid_DoesNotTouchBackend()
    {
        var backend = new FakeBackend();
        var reconciler = new TenantDatabaseReconciler(backend, new FakeMigrations(), NullLogger<TenantDatabaseReconciler>.Instance);
        TenantDatabaseDesiredState desired = DesiredTenant("dedicated", TenantSubscriptionStatus.Active) with { TenantId = "other-tenant" };

        await Assert.ThrowsAsync<ArgumentException>(() => reconciler.ReconcileAsync(desired, TestContext.Current.CancellationToken));
        Assert.Empty(backend.Clusters);
        Assert.Empty(backend.Databases);
    }

    [Fact]
    public async Task ReconcileAsync_WhenDatabaseReadinessIsMissing_DoesNotMigrateOrMarkReady()
    {
        var backend = new FakeBackend { SecretAvailable = true, ClusterReady = true, DatabaseReady = false };
        var migrations = new FakeMigrations();
        var reconciler = new TenantDatabaseReconciler(backend, migrations, NullLogger<TenantDatabaseReconciler>.Instance);

        TenantDatabaseStatus result = await reconciler.ReconcileAsync(DesiredTenant("dedicated", TenantSubscriptionStatus.Active), TestContext.Current.CancellationToken);

        Assert.Equal(TenantDatabasePhase.Provisioning, result.Phase);
        Assert.Empty(migrations.Services);
    }

    private sealed class RecordingReadinessPublisher : ITenantDatabaseReadinessPublisher
    {
        public bool FailNext { get; set; }
        public List<TenantProvisioningReadyIntegrationEvent> Events { get; } = [];
        public Task PublishAsync(TenantProvisioningReadyIntegrationEvent readiness, CancellationToken ct)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Broker temporarily unavailable.");
            }
            Events.Add(readiness);
            return Task.CompletedTask;
        }
    }

    private sealed class UnexpectedFinalBackupRequester : ITenantFinalBackupRequester
    {
        public Task<TenantCompletedFinalBackup?> RequestAsync(TenantFinalBackupTarget target, CancellationToken ct) =>
            throw new InvalidOperationException("Active paid reconciliation must not request a final backup.");
    }

    private sealed class RecordingFinalBackupRequester : ITenantFinalBackupRequester
    {
        public List<TenantFinalBackupTarget> Targets { get; } = [];
        public TenantCompletedFinalBackup? Completed { get; set; }
        public Task<TenantCompletedFinalBackup?> RequestAsync(TenantFinalBackupTarget target, CancellationToken ct)
        {
            Targets.Add(target);
            return Task.FromResult(Completed);
        }
    }

    private sealed class FakeRequestStore : ITenantDatabaseRequestStore
    {
        public List<TenantDatabaseRequest> Pending { get; } = [];
        public Dictionary<Guid, TenantDatabaseStatus> Statuses { get; } = [];
        public HashSet<Guid> Published { get; } = [];
        public Task<bool> UpsertAsync(TenantDatabaseRequest request, CancellationToken ct)
        {
            var prior = Pending.FirstOrDefault(item => item.SubscriptionId == request.SubscriptionId);
            if (prior is not null)
            {
                if (prior.SourceOrderId != request.SourceOrderId || prior.Proof != request.Proof || prior.DesiredState != request.DesiredState)
                {
                    throw new InvalidOperationException("Paid database request snapshot changed.");
                }

                return Task.FromResult(false);
            }

            Pending.Add(request);
            return Task.FromResult(true);
        }
        public Task<bool> AttachEnterprisePolicyAsync(SharedKernel.Events.EnterprisePolicyAttachedIntegrationEvent transition, CancellationToken ct) =>
            throw new InvalidOperationException("Enterprise policy attachment is not used in reconciler worker tests.");

        public Task<bool> SuspendAsync(Guid subscriptionId, Guid sourceOrderId, Guid tenantId, string identifier, DateTimeOffset suspendedAt, string reason, CancellationToken ct)
        {
            int index = Pending.FindIndex(request => request.SubscriptionId == subscriptionId && request.SourceOrderId == sourceOrderId &&
                request.DesiredState.TenantId == tenantId.ToString("D") && request.DesiredState.Identifier == identifier &&
                request.DesiredState.SubscriptionStatus == TenantSubscriptionStatus.Active);
            if (index < 0) return Task.FromResult(false);
            Pending[index] = Pending[index] with { DesiredState = Pending[index].DesiredState with
            {
                SubscriptionStatus = TenantSubscriptionStatus.Suspended, SuspendedAt = suspendedAt,
            } };
            return Task.FromResult(true);
        }
        public Task<IReadOnlyList<TenantDatabaseRequest>> GetPendingAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenantDatabaseRequest>>(Pending.Where(request => !Published.Contains(request.SubscriptionId)).Take(limit).ToArray());
        public async Task<bool> WithCurrentRequestLockAsync(TenantDatabaseRequest request, Func<CancellationToken, Task> action, CancellationToken ct)
        {
            if (!Pending.Contains(request) || Published.Contains(request.SubscriptionId)) return false;
            await action(ct);
            return true;
        }
        public Task SaveStatusAsync(TenantDatabaseRequest request, TenantDatabaseStatus status, DateTimeOffset reconciledAt, CancellationToken ct)
        {
            if (!Pending.Contains(request)) throw new InvalidOperationException("Paid order or desired state changed before readiness could be recorded.");
            Statuses[request.SubscriptionId] = status;
            return Task.CompletedTask;
        }
        public Task MarkReadinessPublishedAsync(TenantDatabaseRequest request, Guid clusterUid, DateTimeOffset publishedAt, CancellationToken ct)
        {
            if (!Pending.Contains(request) || !Statuses.TryGetValue(request.SubscriptionId, out var status) ||
                status.Phase != TenantDatabasePhase.Ready || status.EvidenceId != clusterUid)
                throw new InvalidOperationException("No current ready request to acknowledge.");
            Published.Add(request.SubscriptionId);
            return Task.CompletedTask;
        }
    }
    private static TenantDatabaseDesiredState DesiredTenant(string strategy, TenantSubscriptionStatus status) =>
        new("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "tenant-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", strategy, "postgres", 1, status, null);

    private sealed class FakeBackend : ITenantCnpgBackend
    {
        public List<TenantClusterSpec> Clusters { get; } = [];
        public List<TenantDatabaseSpec> Databases { get; } = [];
        public bool SecretAvailable { get; init; }
        public bool ClusterReady { get; init; }
        public bool EntitlementsReady { get; init; } = true;
        public bool DatabaseReady { get; init; }
        public Task ApplyClusterAsync(TenantClusterSpec spec, CancellationToken ct) { Clusters.Add(spec); return Task.CompletedTask; }
        public Task<bool> HasApplicationSecretAsync(string tenantId, Guid sourceOrderId, CancellationToken ct) => Task.FromResult(SecretAvailable);
        public Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(ClusterReady);
        public Task<bool> ArePaidEntitlementsReadyAsync(TenantClusterSpec spec, CancellationToken ct) => Task.FromResult(EntitlementsReady);
        public Task<Guid?> GetReadyClusterUidAsync(string ns, string name, CancellationToken ct) => Task.FromResult<Guid?>(
            ClusterReady ? Guid.Parse("d0d58a9c-6c2b-4da3-bec0-01fd82ef7251") : null);
        public Task ApplyDatabaseAsync(TenantDatabaseSpec spec, CancellationToken ct) { Databases.Add(spec); return Task.CompletedTask; }
        public Task<bool> IsDatabaseReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(DatabaseReady);
        public Task SuspendAccessAsync(string tenantId, string identifier, Guid sourceOrderId, DateTimeOffset suspendedAt, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeMigrations : ITenantSchemaMigrator
    {
        public List<string> Services { get; } = [];
        public Task MigrateAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, Guid previousSourceOrderId, string service, CancellationToken ct)
        {
            Services.Add(service);
            return Task.CompletedTask;
        }
    }
}

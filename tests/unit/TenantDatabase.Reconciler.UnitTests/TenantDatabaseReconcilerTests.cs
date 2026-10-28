using Microsoft.Extensions.Logging.Abstractions;
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
            PlanId = "Business", PlanVersion = "2026-10", Currency = "USD", AmountMinor = 10000,
            ValidFromUtc = DateTimeOffset.UtcNow, ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(30),
            ConfirmedAtUtc = DateTimeOffset.UtcNow,
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
        await intake.Handle(paid, CancellationToken.None);
        Assert.Single(store.Pending);

        paid.ReplicaCount = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => intake.Handle(paid, CancellationToken.None));
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenRequestIsPersisted_ReconcilesAndPersistsExternallyVisiblePhase()
    {
        var backend = new FakeBackend { SecretAvailable = true, ClusterReady = true, DatabaseReady = true };
        var store = new FakeRequestStore();
        var reconciler = new TenantDatabaseReconciler(backend, new FakeMigrations(), NullLogger<TenantDatabaseReconciler>.Instance);
        var worker = new TenantDatabaseWorker(store, reconciler, NullLogger<TenantDatabaseWorker>.Instance);
        TenantDatabaseRequest request = new(Guid.NewGuid(), DesiredTenant("dedicated", TenantSubscriptionStatus.Active), DateTimeOffset.UtcNow);
        store.Pending.Add(request);

        int processed = await worker.ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, processed);
        Assert.Equal(TenantDatabasePhase.Ready, store.Statuses[request.SubscriptionId].Phase);
        Assert.Equal(request.DesiredState.TenantId, store.Statuses[request.SubscriptionId].TenantId);
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
    public async Task ReconcileAsync_WhenCredentialsAndDatabaseAreReady_RunsMigrationBeforeReady()
    {
        var backend = new FakeBackend { SecretAvailable = true, ClusterReady = true, DatabaseReady = true };
        var migrations = new FakeMigrations();
        var reconciler = new TenantDatabaseReconciler(backend, migrations, NullLogger<TenantDatabaseReconciler>.Instance);
        TenantDatabaseDesiredState desired = DesiredTenant("dedicated", TenantSubscriptionStatus.Active);

        TenantDatabaseStatus result = await reconciler.ReconcileAsync(desired, TestContext.Current.CancellationToken);

        Assert.Equal(TenantDatabasePhase.Ready, result.Phase);
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

    private sealed class FakeRequestStore : ITenantDatabaseRequestStore
    {
        public List<TenantDatabaseRequest> Pending { get; } = [];
        public Dictionary<Guid, TenantDatabaseStatus> Statuses { get; } = [];
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
        public Task<bool> SuspendAsync(Guid subscriptionId, Guid sourceOrderId, Guid tenantId, string identifier, DateTimeOffset suspendedAt, CancellationToken ct)
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
        public Task<IReadOnlyList<TenantDatabaseRequest>> GetPendingAsync(int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<TenantDatabaseRequest>>(Pending.Take(limit).ToArray());
        public Task SaveStatusAsync(TenantDatabaseRequest request, TenantDatabaseStatus status, DateTimeOffset reconciledAt, CancellationToken ct)
        {
            if (!Pending.Contains(request)) throw new InvalidOperationException("Paid order or desired state changed before readiness could be recorded.");
            Statuses[request.SubscriptionId] = status;
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
        public bool DatabaseReady { get; init; }
        public Task ApplyClusterAsync(TenantClusterSpec spec, CancellationToken ct) { Clusters.Add(spec); return Task.CompletedTask; }
        public Task<bool> HasApplicationSecretAsync(string tenantId, CancellationToken ct) => Task.FromResult(SecretAvailable);
        public Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(ClusterReady);
        public Task ApplyDatabaseAsync(TenantDatabaseSpec spec, CancellationToken ct) { Databases.Add(spec); return Task.CompletedTask; }
        public Task<bool> IsDatabaseReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(DatabaseReady);
        public Task SuspendAccessAsync(string tenantId, DateTimeOffset suspendedAt, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeMigrations : ITenantSchemaMigrator
    {
        public List<string> Services { get; } = [];
        public Task MigrateAsync(TenantDatabaseDesiredState state, string service, CancellationToken ct)
        {
            Services.Add(service);
            return Task.CompletedTask;
        }
    }
}

using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class TenantDatabaseWorkerConcurrencyTests(SharedTestcontainersFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewPaidPeriodOrSuspension_WaitsForInFlightPaidReconcileBeforeChangingDurableDesiredState(bool paidSuccessor)
    {
        string database = "tenant_lock_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var dataSource = NpgsqlDataSource.Create(fixture.GetDatabaseConnectionString(database));
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var migrate = connection.CreateCommand())
            {
                migrate.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
                    "tenant-database", "001_create_tenant_database_requests.sql"));
                await migrate.ExecuteNonQueryAsync();
            }
            var store = new PostgresTenantDatabaseRequestStore(dataSource);
            Guid tenant = Guid.NewGuid(), subscription = Guid.NewGuid(), order = Guid.NewGuid();
            const string identifier = "concurrent-paid-business";
            var desired = new TenantDatabaseDesiredState(tenant.ToString("D"), identifier,
                "dedicated", "postgres", 1, TenantSubscriptionStatus.Active, null);
            Assert.True(await store.UpsertAsync(new(subscription, desired, DateTimeOffset.UtcNow, order, "signed"), CancellationToken.None));
            var backend = new DelayedClusterBackend();
            var reconciler = new TenantDatabaseReconciler(backend, new UnusedMigrator(),
                NullLogger<TenantDatabaseReconciler>.Instance);
            var worker = new TenantDatabaseWorker(store, reconciler, new UnusedReadinessPublisher(),
                new DatabaseReadinessSigningKey(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                NullLogger<TenantDatabaseWorker>.Instance);
            Task processing = worker.ProcessBatchAsync(CancellationToken.None);
            await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Guid nextOrder = Guid.NewGuid();
            Task<bool> changed = paidSuccessor
                ? store.UpsertAsync(new(subscription, desired, DateTimeOffset.UtcNow, nextOrder, "successor-signed", order), CancellationToken.None)
                : store.SuspendAsync(subscription, order, tenant, identifier, DateTimeOffset.UtcNow, CancellationToken.None);
            try
            {
                await Task.Delay(300);
                Assert.False(changed.IsCompleted, "A new paid period or suspension cannot overtake an in-flight reconciliation.");
            }
            finally
            {
                backend.Continue.TrySetResult();
                await processing;
            }
            Assert.True(await changed.WaitAsync(TimeSpan.FromSeconds(10)));
            var current = Assert.Single(await store.GetPendingAsync(32, CancellationToken.None),
                item => item.SubscriptionId == subscription);
            Assert.Equal(paidSuccessor ? nextOrder : order, current.SourceOrderId);
            Assert.Equal(paidSuccessor ? TenantSubscriptionStatus.Active : TenantSubscriptionStatus.Suspended,
                current.DesiredState.SubscriptionStatus);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class DelayedClusterBackend : ITenantCnpgBackend
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ApplyClusterAsync(TenantClusterSpec spec, CancellationToken ct)
        {
            Started.TrySetResult();
            await Continue.Task.WaitAsync(ct);
        }
        public Task<bool> HasApplicationSecretAsync(string tenantId, Guid sourceOrderId, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(false);
        public Task<Guid?> GetReadyClusterUidAsync(string ns, string name, CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task ApplyDatabaseAsync(TenantDatabaseSpec spec, CancellationToken ct) => throw new InvalidOperationException("No migration before credentials.");
        public Task<bool> IsDatabaseReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(false);
        public Task SuspendAccessAsync(string tenantId, string identifier, Guid sourceOrderId, DateTimeOffset suspendedAt, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class UnusedMigrator : ITenantSchemaMigrator
    {
        public Task MigrateAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, Guid previousSourceOrderId,
            string service, CancellationToken ct) => throw new InvalidOperationException("No migrations before credentials.");
    }
    private sealed class UnusedReadinessPublisher : ITenantDatabaseReadinessPublisher
    {
        public Task PublishAsync(TenantProvisioningReadyIntegrationEvent readiness, CancellationToken ct) =>
            throw new InvalidOperationException("No readiness before credentials.");
    }
}

extern alias TenantDatabaseHost;

using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Npgsql;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Proves source-order-fenced retention and audit completion in the real PostgreSQL worker store.</summary>
[Collection("SharedTestcontainers")]
public sealed class TenantDatabasePurgeStoreTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task ApprovedRetainedPeriod_PurgesOnceAfterRecoveryEvidenceAndRejectsPaidSuccessor()
    {
        string database = "tenant_purge_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var source = NpgsqlDataSource.Create(fixture.GetDatabaseConnectionString(database));
            await using (var db = new TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext>()
                    .UseNpgsql(fixture.GetDatabaseConnectionString(database)).Options))
                await db.Database.MigrateAsync();

            var store = new PostgresTenantDatabaseRequestStore(source);
            var tenant = Guid.NewGuid();
            var subscription = Guid.NewGuid();
            var paidOrder = Guid.NewGuid();
            var clusterUid = Guid.NewGuid();
            var suspendedAt = DateTimeOffset.UtcNow.AddDays(-31);
            var desired = new TenantDatabaseDesiredState(tenant.ToString("D"), $"tenant-{tenant:N}",
                "dedicated", "postgres", 1, TenantSubscriptionStatus.Active, null);
            var request = new TenantDatabaseRequest(subscription, desired, DateTimeOffset.UtcNow.AddYears(-1), paidOrder, "signed");
            Assert.True(await store.UpsertAsync(request, CancellationToken.None));
            await store.SaveStatusAsync(request, new(tenant.ToString("D"), TenantDatabasePhase.Ready, EvidenceId: clusterUid),
                DateTimeOffset.UtcNow.AddDays(-32), CancellationToken.None);
            Assert.True(await store.SuspendAsync(subscription, paidOrder, tenant, desired.Identifier, suspendedAt, CancellationToken.None));
            var retained = Assert.Single(await store.GetPendingAsync(10, CancellationToken.None));
            await store.SaveStatusAsync(retained, new(tenant.ToString("D"), TenantDatabasePhase.Retained), DateTimeOffset.UtcNow,
                CancellationToken.None);

            var approval = new TenantPurgeApprovedIntegrationEvent
            {
                ApprovalId = Guid.NewGuid(), TenantId = tenant, TenantIdentifier = desired.Identifier,
                SubscriptionId = subscription, SourceOrderId = paidOrder, SuspendedAtUtc = suspendedAt,
                EligibleAtUtc = suspendedAt.AddDays(30), ApprovedAtUtc = DateTimeOffset.UtcNow,
                FirstApproverSubjectId = "manager-one", SecondApproverSubjectId = "manager-two",
            };
            using RSA signer = RSA.Create(2048);
            using var keys = new TenantPurgeVerificationKeys([signer.ExportSubjectPublicKeyInfoPem()]);
            approval.Proof = TenantPurgeApprovalProof.Sign(approval, signer);
            await new SignedTenantPurgeIntake(store, keys, new TenantPurgeRetentionPolicy(TimeSpan.FromDays(30))).Handle(approval, CancellationToken.None);
            Assert.Equal(approval.ApprovalId,
                Assert.Single(await store.GetPendingPurgeApprovalsAsync(10, CancellationToken.None)).ApprovalId);
            await new SignedTenantPurgeIntake(store, keys, new TenantPurgeRetentionPolicy(TimeSpan.FromDays(30))).Handle(approval, CancellationToken.None);
            bool executed = await store.WithCurrentRetainedRequestLockAsync(approval, TimeSpan.FromDays(30),
                async (target, _) =>
                {
                    Assert.Equal(clusterUid, target.ClusterUid);
                    Assert.Equal(paidOrder, target.SourceOrderId);
                    await store.RecordVerifiedRecoveryAsync(approval,
                        new TenantBackupRecoveryEvidence(Guid.NewGuid(), Guid.NewGuid(), "backup-test", DateTimeOffset.UtcNow),
                        CancellationToken.None);
                }, CancellationToken.None);
            Assert.True(executed);
            Assert.Equal(TenantDatabasePhase.Purged, (await store.GetStatusAsync(subscription, CancellationToken.None))!.Phase);
            Assert.False(await store.WithCurrentRetainedRequestLockAsync(approval, TimeSpan.FromDays(30),
                (_, _) => throw new Xunit.Sdk.XunitException("Duplicate purge must not re-delete."), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpsertAsync(
                new(subscription, desired, DateTimeOffset.UtcNow, Guid.NewGuid(), "signed-successor", paidOrder), CancellationToken.None));
            approval.SourceOrderId = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.WithCurrentRetainedRequestLockAsync(approval,
                TimeSpan.FromDays(30), (_, _) => Task.CompletedTask, CancellationToken.None));
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

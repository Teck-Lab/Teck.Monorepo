extern alias TenantDatabaseHost;

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Exercises signed tenant database intake and durable SQL replay protection.</summary>
[Collection("SharedTestcontainers")]
public sealed class TenantDatabaseRequestStoreTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task Signed_paid_request_survives_polling_and_rejects_changed_order_or_tenant()
    {
        var connectionString = fixture.GetDatabaseConnectionString("testdb_tenant_database_requests");
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = "SELECT 1 FROM pg_database WHERE datname = 'testdb_tenant_database_requests'";
            if (await create.ExecuteScalarAsync() is null)
            {
                create.CommandText = "CREATE DATABASE testdb_tenant_database_requests";
                await create.ExecuteNonQueryAsync();
            }
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var migration = connection.CreateCommand())
        {
            await using var db = new TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext>()
                    .UseNpgsql(connectionString).Options);
            await db.Database.MigrateAsync();
        }

        var store = new PostgresTenantDatabaseRequestStore(dataSource);
        using var privateKey = RSA.Create(2048);
        using var keys = new SubscriptionPaymentVerificationKeys([privateKey.ExportSubjectPublicKeyInfoPem()]);
        var intake = new SignedTenantDatabaseIntake(store, keys);
        var now = DateTimeOffset.UtcNow;
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TenantIdentifier = "real-postgres-test-tenant", PaymentMethod = "captured", DatabaseStrategy = "dedicated",
            DatabaseProvider = "postgres", PlanId = "Business", PlanVersion = "2026-10", AmountMinor = 10000,
            Currency = "USD", ReplicaCount = 1, HasReadReplicas = true, HasScheduledBackups = true,
            HasHighAvailability = false, ConfirmedAtUtc = now,
            ValidFromUtc = now, ValidUntilUtc = now.AddDays(365),
            PolicyVersion = 4, EffectiveGracePeriodDays = 7, EffectiveBackupRetentionDays = 30,
        };
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);

        await intake.Handle(paid, CancellationToken.None);
        await intake.Handle(paid, CancellationToken.None);
        var requests = await store.GetPendingAsync(32, CancellationToken.None);
        var recorded = Assert.Single(requests, request => request.SubscriptionId == paid.SubscriptionId);
        Assert.Equal(paid.SourceOrderId, recorded.SourceOrderId);
        Assert.Equal(paid.Proof, recorded.Proof);
        Assert.Equal(paid.ReplicaCount, recorded.DesiredState.ReadReplicas);
        Assert.Equal(paid.PreviousSourceOrderId, recorded.PreviousSourceOrderId);
        Assert.Equal(paid.HasScheduledBackups, recorded.DesiredState.HasScheduledBackups);
        Assert.Equal(paid.HasHighAvailability, recorded.DesiredState.HasHighAvailability);
        Assert.Equal(paid.ValidUntilUtc, recorded.DesiredState.PaidUntilUtc);
        Assert.Equal(7, recorded.DesiredState.GracePeriodDays);
        Assert.Equal(30, recorded.DesiredState.BackupRetentionDays);

        var clusterUid = Guid.NewGuid();
        await store.SaveStatusAsync(recorded,
            new TenantDatabaseStatus(recorded.DesiredState.TenantId, TenantDatabasePhase.Ready, "healthy", clusterUid),
            now, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MarkReadinessPublishedAsync(recorded,
            Guid.NewGuid(), now, CancellationToken.None));
        await store.MarkReadinessPublishedAsync(recorded, clusterUid, now, CancellationToken.None);
        Assert.DoesNotContain(await store.GetPendingAsync(32, CancellationToken.None),
            request => request.SubscriptionId == paid.SubscriptionId);

        var firstOrder = paid.SourceOrderId;
        var nextOrder = Guid.NewGuid();
        paid.PreviousSourceOrderId = firstOrder;
        paid.SourceOrderId = nextOrder;
        paid.ConfirmedAtUtc = now.AddDays(30);
        paid.ValidFromUtc = now.AddDays(365);
        paid.ValidUntilUtc = paid.ValidFromUtc.AddYears(1);
        paid.HasScheduledBackups = false;
        paid.HasHighAvailability = true;
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        await intake.Handle(paid, CancellationToken.None);
        await intake.Handle(paid, CancellationToken.None);
        var renewed = await store.GetPendingAsync(32, CancellationToken.None);
        var renewedRecord = Assert.Single(renewed, request => request.SubscriptionId == paid.SubscriptionId);
        Assert.Equal(nextOrder, renewedRecord.SourceOrderId);
        Assert.Equal(firstOrder, renewedRecord.PreviousSourceOrderId);
        Assert.Equal(paid.HasScheduledBackups, renewedRecord.DesiredState.HasScheduledBackups);
        Assert.Equal(paid.HasHighAvailability, renewedRecord.DesiredState.HasHighAvailability);

        var staleSuspension = new SubscriptionAccessSuspendedIntegrationEvent
        {
            SourceOrderId = firstOrder, SubscriptionId = paid.SubscriptionId, TenantId = paid.TenantId,
            TenantIdentifier = paid.TenantIdentifier, Reason = "renewal_failed", SuspendedAtUtc = now.AddDays(365),
        };
        staleSuspension.Proof = SubscriptionSuspensionProof.Sign(staleSuspension, privateKey);
        await new SignedTenantDatabaseSuspensionIntake(store, keys).Handle(staleSuspension, CancellationToken.None);
        Assert.Equal(TenantDatabasePhase.Pending, (await store.GetStatusAsync(paid.SubscriptionId, CancellationToken.None))!.Phase);

        var currentSuspension = new SubscriptionAccessSuspendedIntegrationEvent
        {
            SourceOrderId = nextOrder, SubscriptionId = paid.SubscriptionId, TenantId = paid.TenantId,
            TenantIdentifier = paid.TenantIdentifier, Reason = "cancelled", SuspendedAtUtc = now.AddDays(31),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SignedTenantDatabaseSuspensionIntake(store, keys)
            .Handle(currentSuspension, CancellationToken.None));
        currentSuspension.Proof = SubscriptionSuspensionProof.Sign(currentSuspension, privateKey);
        await new SignedTenantDatabaseSuspensionIntake(store, keys).Handle(currentSuspension, CancellationToken.None);
        var suspended = Assert.Single(await store.GetPendingAsync(32, CancellationToken.None),
            request => request.SubscriptionId == paid.SubscriptionId);
        Assert.Equal(TenantSubscriptionStatus.Suspended, suspended.DesiredState.SubscriptionStatus);
        Assert.Equal(paid.ValidUntilUtc, suspended.DesiredState.PaidUntilUtc);
        Assert.Equal(paid.EffectiveGracePeriodDays, suspended.DesiredState.GracePeriodDays);
        Assert.Equal(paid.EffectiveBackupRetentionDays, suspended.DesiredState.BackupRetentionDays);
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var reason = connection.CreateCommand())
        {
            reason.CommandText = "SELECT suspension_reason FROM tenant_database_requests WHERE subscription_id = @subscription";
            reason.Parameters.AddWithValue("subscription", paid.SubscriptionId);
            Assert.Equal("cancelled", (string?)await reason.ExecuteScalarAsync());
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveStatusAsync(renewedRecord,
            new TenantDatabaseStatus(paid.TenantId.ToString("D"), TenantDatabasePhase.Ready),
            DateTimeOffset.UtcNow, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveStatusAsync(recorded,
            new TenantDatabaseStatus(paid.TenantId.ToString("D"), TenantDatabasePhase.Ready),
            DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(TenantDatabasePhase.Pending, (await store.GetStatusAsync(paid.SubscriptionId, CancellationToken.None))!.Phase);

        paid.PreviousSourceOrderId = Guid.NewGuid();
        paid.SourceOrderId = Guid.NewGuid();
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => intake.Handle(paid, CancellationToken.None));

        paid.PreviousSourceOrderId = Guid.Empty;
        paid.SourceOrderId = firstOrder;
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => intake.Handle(paid, CancellationToken.None));
        await using var verification = await dataSource.OpenConnectionAsync();
        await using var query = verification.CreateCommand();
        query.CommandText = "SELECT source_order_id FROM tenant_database_requests WHERE subscription_id = @subscription";
        query.Parameters.AddWithValue("subscription", paid.SubscriptionId);
        Assert.Equal(nextOrder, (Guid)(await query.ExecuteScalarAsync())!);
    }
    [Fact]
    public async Task Enterprise_policy_transition_updates_current_request_and_accepts_only_exact_replay()
    {
        string database = "tenant_policy_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            // Database identifiers are generated internally and cannot be supplied by a test caller.
            create.CommandText = $"CREATE DATABASE \"{database}\"";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            string connectionString = fixture.GetDatabaseConnectionString(database);
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await using (var db = new TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext(
                new DbContextOptionsBuilder<TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext>()
                    .UseNpgsql(connectionString).Options))
            {
                await db.Database.MigrateAsync();
            }
            var store = new PostgresTenantDatabaseRequestStore(dataSource);
            using var billingKey = RSA.Create(2048);
            using var licensingKey = RSA.Create(2048);
            using var billingKeys = new SubscriptionPaymentVerificationKeys([billingKey.ExportSubjectPublicKeyInfoPem()]);
            using var licensingKeys = new EnterprisePolicyDocumentVerificationKeys(
                [new KeyValuePair<string, string?>("licensing-key", licensingKey.ExportSubjectPublicKeyInfoPem())]);
            var paidIntake = new SignedTenantDatabaseIntake(store, billingKeys);
            var now = DateTimeOffset.UtcNow;
            var paid = new SubscriptionPaymentConfirmedIntegrationEvent
            {
                SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
                TenantIdentifier = "tenant-policy-test", PaymentMethod = "captured", DatabaseStrategy = "dedicated",
                DatabaseProvider = "postgres", PlanId = "Business", PlanVersion = "2026-10", AmountMinor = 10000,
                Currency = "USD", ReplicaCount = 1, HasReadReplicas = true, HasScheduledBackups = true,
                ConfirmedAtUtc = now, ValidFromUtc = now, ValidUntilUtc = now.AddYears(1),
                PolicyVersion = 4, EffectiveGracePeriodDays = 7, EffectiveBackupRetentionDays = 30,
            };
            paid.Proof = SubscriptionPaymentProof.Sign(paid, billingKey);
            await paidIntake.Handle(paid, CancellationToken.None);

            var transition = CreateSignedTransition(paid.TenantId, paid.TenantIdentifier, paid.SubscriptionId,
                paid.SourceOrderId, now, billingKey, licensingKey);
            var rejectedTransition = CreateSignedTransition(paid.TenantId, paid.TenantIdentifier, paid.SubscriptionId,
                paid.SourceOrderId, now, billingKey, licensingKey);
            var rejectingIntake = new EnterprisePolicyAttachedIntake(store, billingKeys, licensingKeys,
                new RejectingPolicyVerifier());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                rejectingIntake.Handle(rejectedTransition, CancellationToken.None));
            var unchangedStatus = await store.GetStatusAsync(paid.SubscriptionId, CancellationToken.None);
            Assert.Equal(TenantDatabasePhase.Pending, unchangedStatus?.Phase);

            var policyIntake = new EnterprisePolicyAttachedIntake(store, billingKeys, licensingKeys, new ActivePolicyVerifier());
            await Task.WhenAll(
                policyIntake.Handle(transition, CancellationToken.None),
                policyIntake.Handle(transition, CancellationToken.None));
            var attached = Assert.Single(await store.GetPendingAsync(32, CancellationToken.None),
                request => request.SubscriptionId == paid.SubscriptionId);
            Assert.Equal(45, attached.DesiredState.GracePeriodDays);
            Assert.Equal(120, attached.DesiredState.BackupRetentionDays);
            Assert.Equal("enterprise", attached.DesiredState.EnterprisePolicyId);
            Assert.Equal("v1", attached.DesiredState.EnterprisePolicyVersion);
            Assert.Equal(paid.SourceOrderId, attached.DesiredState.EnterprisePolicySourceOrderId);
            Assert.Equal(7, paid.EffectiveGracePeriodDays);
            Assert.Equal(30, paid.EffectiveBackupRetentionDays);

            await policyIntake.Handle(transition, CancellationToken.None);

            var invalidTransitions = new[]
            {
                CreateSignedTransition(paid.TenantId, paid.TenantIdentifier, paid.SubscriptionId, Guid.NewGuid(), now, billingKey, licensingKey),
                CreateSignedTransition(Guid.NewGuid(), paid.TenantIdentifier, paid.SubscriptionId, paid.SourceOrderId, now, billingKey, licensingKey),
                CreateSignedTransition(paid.TenantId, paid.TenantIdentifier, paid.SubscriptionId, paid.SourceOrderId, now, billingKey, licensingKey,
                    gracePeriodDays: transition.GracePeriodDays + 1),
                CreateSignedTransition(paid.TenantId, paid.TenantIdentifier, paid.SubscriptionId, paid.SourceOrderId, now, billingKey, licensingKey,
                    backupRetentionDays: transition.BackupRetentionDays + 1),
                CreateSignedTransition(paid.TenantId, paid.TenantIdentifier, paid.SubscriptionId, paid.SourceOrderId, now, billingKey, licensingKey,
                    policyVersion: "v2"),
                CreateSignedTransition(paid.TenantId, paid.TenantIdentifier, paid.SubscriptionId, paid.SourceOrderId, now, billingKey, licensingKey,
                    policyId: "different-policy"),
                CreateSignedTransition(paid.TenantId, "different-tenant", paid.SubscriptionId, paid.SourceOrderId, now, billingKey, licensingKey),
            };
            foreach (var invalid in invalidTransitions)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => policyIntake.Handle(invalid, CancellationToken.None));
            }

            var missingOrder = CreateSignedTransition(paid.TenantId, paid.TenantIdentifier, Guid.NewGuid(), paid.SourceOrderId,
                now, billingKey, licensingKey);

            await Assert.ThrowsAsync<InvalidOperationException>(() => policyIntake.Handle(missingOrder, CancellationToken.None));
            await using var verify = await dataSource.OpenConnectionAsync();
            await using var query = verify.CreateCommand();
            query.CommandText = "SELECT desired_state::text, source_order_id, proof, (SELECT count(*) FROM tenant_database_requests WHERE subscription_id = @missing) FROM tenant_database_requests WHERE subscription_id = @subscription";
            query.Parameters.AddWithValue("missing", missingOrder.SubscriptionId);
            query.Parameters.AddWithValue("subscription", paid.SubscriptionId);
            await using var result = await query.ExecuteReaderAsync();
            Assert.True(await result.ReadAsync());
            var unchanged = System.Text.Json.JsonSerializer.Deserialize<TenantDatabaseDesiredState>(result.GetString(0));
            Assert.Equal(attached.DesiredState, unchanged);
            Assert.Equal(attached.SourceOrderId, result.GetGuid(1));
            Assert.Equal(attached.Proof, result.GetString(2));
            Assert.Equal(0L, result.GetInt64(3));
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static EnterprisePolicyAttachedIntegrationEvent CreateSignedTransition(Guid tenantId, string tenantIdentifier,
        Guid subscriptionId, Guid sourceOrderId, DateTimeOffset now, RSA billingKey, RSA licensingKey,
        int gracePeriodDays = 45, int backupRetentionDays = 120, string policyId = "enterprise", string policyVersion = "v1") =>
        Resign(new EnterprisePolicyAttachedIntegrationEvent
        {
            TenantId = tenantId, TenantIdentifier = tenantIdentifier, SubscriptionId = subscriptionId, SourceOrderId = sourceOrderId,
            PolicyId = policyId, PolicyVersion = policyVersion, GracePeriodDays = gracePeriodDays, BackupRetentionDays = backupRetentionDays,
            LicenseKind = "custom_enterprise", LicensingSigningKeyId = "licensing-key",
            ValidFromUtc = now.AddMinutes(-1), ValidUntilUtc = now.AddDays(30),
        }, billingKey, licensingKey);

    private static EnterprisePolicyAttachedIntegrationEvent Resign(EnterprisePolicyAttachedIntegrationEvent transition,
        RSA billingKey, RSA licensingKey)
    {
        var document = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            policy_id = transition.PolicyId, policy_version = transition.PolicyVersion, tenant_id = transition.TenantId,
            subscription_id = transition.SubscriptionId, source_order_id = transition.SourceOrderId,
            grace_period_days = transition.GracePeriodDays, backup_retention_days = transition.BackupRetentionDays,
            valid_from_utc = transition.ValidFromUtc.ToUniversalTime().ToString("O"),
            valid_until_utc = transition.ValidUntilUtc.ToUniversalTime().ToString("O"),
            license_kind = transition.LicenseKind, signing_key_id = transition.LicensingSigningKeyId,
        });
        transition.LicensingSignature = Convert.ToBase64String(
            licensingKey.SignData(document, System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pss));
        transition.Proof = EnterprisePolicyAttachedProof.Sign(transition, billingKey);
        return transition;
    }


    [Fact]
    public async Task Retained_failed_renewal_wakes_after_signed_grace_but_cancellation_never_wakes()
    {
        var connectionString = fixture.GetDatabaseConnectionString("testdb_tenant_database_retention");
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = "SELECT 1 FROM pg_database WHERE datname = 'testdb_tenant_database_retention'";
            if (await create.ExecuteScalarAsync() is null)
            {
                create.CommandText = "CREATE DATABASE testdb_tenant_database_retention";
                await create.ExecuteNonQueryAsync();
            }
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var db = new TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext(
            new DbContextOptionsBuilder<TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext>()
                .UseNpgsql(connectionString).Options))
            await db.Database.MigrateAsync();

        var store = new PostgresTenantDatabaseRequestStore(dataSource);
        var now = DateTimeOffset.UtcNow;
        var tenant = Guid.NewGuid();
        var order = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var desired = new TenantDatabaseDesiredState(tenant.ToString("D"), "grace-tenant", "dedicated", "postgres", 0,
            TenantSubscriptionStatus.Active, null, PaidUntilUtc: now.AddDays(-8), GracePeriodDays: 7, BackupRetentionDays: 30);
        var paid = new TenantDatabaseRequest(subscription, desired, now.AddDays(-365), order, "signed");
        Assert.True(await store.UpsertAsync(paid, CancellationToken.None));
        var recordedUid = Guid.NewGuid();
        await store.SaveStatusAsync(paid, new(tenant.ToString("D"), TenantDatabasePhase.Ready, EvidenceId: recordedUid),
            now.AddDays(-8), CancellationToken.None);
        Assert.True(await store.SuspendAsync(subscription, order, tenant, desired.Identifier, now.AddDays(-8), "renewal_failed", CancellationToken.None));
        var suspended = Assert.Single(await store.GetPendingAsync(10, CancellationToken.None));
        await store.SaveStatusAsync(suspended, new(tenant.ToString("D"), TenantDatabasePhase.Retained), now, CancellationToken.None);
        Assert.DoesNotContain(await store.GetPendingAsync(10, CancellationToken.None), request => request.SubscriptionId == subscription);
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var release = connection.CreateCommand())
        {
            release.CommandText = "UPDATE tenant_database_requests SET lease_until = now() - interval '1 second' WHERE subscription_id = @subscription";
            release.Parameters.AddWithValue("subscription", subscription);
            await release.ExecuteNonQueryAsync();
        }
        var due = Assert.Single(await store.GetPendingAsync(10, CancellationToken.None), request => request.SubscriptionId == subscription);
        Assert.Equal(recordedUid, due.LastClusterUid);
        Assert.True(due.IsDueRetainedRenewal);

        var executed = false;
        Assert.True(await store.WithCurrentRequestLockAsync(due, _ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.True(executed);
        var successor = new TenantDatabaseRequest(subscription, desired with
        {
            PaidUntilUtc = now.AddDays(365), SubscriptionStatus = TenantSubscriptionStatus.Active, SuspendedAt = null,
        }, now, Guid.NewGuid(), "successor-signed", order);
        Assert.True(await store.UpsertAsync(successor, CancellationToken.None));
        Assert.False(await store.WithCurrentRequestLockAsync(due, _ =>
            throw new InvalidOperationException("A stale paid order must never trigger final backup."), CancellationToken.None));

        var notDueTenant = Guid.NewGuid();
        var notDue = new TenantDatabaseRequest(Guid.NewGuid(), desired with
        {
            TenantId = notDueTenant.ToString("D"), Identifier = "not-due-tenant", PaidUntilUtc = now.AddDays(-6),
        }, now.AddDays(-365), Guid.NewGuid(), "signed");
        Assert.True(await store.UpsertAsync(notDue, CancellationToken.None));
        Assert.True(await store.SuspendAsync(notDue.SubscriptionId, notDue.SourceOrderId, notDueTenant,
            notDue.DesiredState.Identifier, now.AddDays(-6), "renewal_failed", CancellationToken.None));
        var pendingGrace = Assert.Single(await store.GetPendingAsync(10, CancellationToken.None),
            request => request.SubscriptionId == notDue.SubscriptionId);
        await store.SaveStatusAsync(pendingGrace, new(notDueTenant.ToString("D"), TenantDatabasePhase.Retained), now, CancellationToken.None);
        Assert.DoesNotContain(await store.GetPendingAsync(10, CancellationToken.None), request => request.SubscriptionId == notDue.SubscriptionId);

        var unknownTenant = Guid.NewGuid();
        var unknown = new TenantDatabaseRequest(Guid.NewGuid(), desired with
        {
            TenantId = unknownTenant.ToString("D"), Identifier = "unknown-cluster-tenant",
        }, now.AddDays(-365), Guid.NewGuid(), "signed");
        Assert.True(await store.UpsertAsync(unknown, CancellationToken.None));
        Assert.True(await store.SuspendAsync(unknown.SubscriptionId, unknown.SourceOrderId, unknownTenant,
            unknown.DesiredState.Identifier, now.AddDays(-8), "renewal_failed", CancellationToken.None));
        var initial = Assert.Single(await store.GetPendingAsync(10, CancellationToken.None),
            request => request.SubscriptionId == unknown.SubscriptionId);
        await store.SaveStatusAsync(initial, new(unknownTenant.ToString("D"), TenantDatabasePhase.Retained), now, CancellationToken.None);
        Assert.DoesNotContain(await store.GetPendingAsync(10, CancellationToken.None), request => request.SubscriptionId == unknown.SubscriptionId);

        var cancelledTenant = Guid.NewGuid();
        var cancelled = new TenantDatabaseRequest(Guid.NewGuid(), desired with { TenantId = cancelledTenant.ToString("D"), Identifier = "cancelled-tenant" },
            now.AddDays(-365), Guid.NewGuid(), "signed");
        Assert.True(await store.UpsertAsync(cancelled, CancellationToken.None));
        Assert.True(await store.SuspendAsync(cancelled.SubscriptionId, cancelled.SourceOrderId, cancelledTenant,
            cancelled.DesiredState.Identifier, now.AddDays(-8), "cancelled", CancellationToken.None));
        var cancellation = Assert.Single(await store.GetPendingAsync(10, CancellationToken.None),
            request => request.SubscriptionId == cancelled.SubscriptionId);
        await store.SaveStatusAsync(cancellation, new(cancelledTenant.ToString("D"), TenantDatabasePhase.Retained), now, CancellationToken.None);

        Assert.DoesNotContain(await store.GetPendingAsync(10, CancellationToken.None), request => request.SubscriptionId == cancelled.SubscriptionId);
    }

    private sealed class RejectingPolicyVerifier : IEnterprisePolicyFreshnessVerifier
    {
        public Task VerifyAsync(EnterprisePolicyAttachedIntegrationEvent transition, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Licensing policy is unavailable.");
    }

    private sealed class ActivePolicyVerifier : IEnterprisePolicyFreshnessVerifier
    {
        public Task VerifyAsync(EnterprisePolicyAttachedIntegrationEvent transition, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

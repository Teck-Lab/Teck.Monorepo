extern alias TenantDatabaseHost;

using Microsoft.EntityFrameworkCore;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Exercises read-only nonpayment retention deadline eligibility against real PostgreSQL.</summary>
[Collection("SharedTestcontainers")]
public sealed class TenantDatabaseAutomaticExpiryCandidateTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task ExpiryCandidates_UseTrustedGraceAndRetentionTermsAndExcludeCancellation()
    {
        await using NpgsqlDataSource source = await CreateDatabaseAsync();
        var store = new PostgresTenantDatabaseRequestStore(source);
        DateTimeOffset databaseNow = await GetDatabaseNowAsync(source);

        var standardDue = await AddRetainedRequestAsync(store, source, "standard-due", databaseNow.AddDays(-37), 7, 30, "renewal_failed");
        _ = await AddRetainedRequestAsync(store, source, "standard-not-due", databaseNow.AddDays(-36), 7, 30, "renewal_failed");
        var enterpriseDue = await AddRetainedRequestAsync(store, source, "enterprise-due", databaseNow.AddDays(-166), 45, 120, "renewal_failed");
        _ = await AddRetainedRequestAsync(store, source, "enterprise-not-due", databaseNow.AddDays(-164), 45, 120, "renewal_failed");
        _ = await AddRetainedRequestAsync(store, source, "cancelled", databaseNow.AddDays(-400), 7, 30, "cancelled");
        var exactBoundary = await AddRetainedRequestAsync(store, source, "exact-boundary", databaseNow.AddDays(-37), 7, 30, "renewal_failed");
        var justBefore = await AddRetainedRequestAsync(store, source, "just-before", databaseNow.AddDays(-37), 7, 30, "renewal_failed");
        await SetExactEligibleAfterAsync(source, exactBoundary.SubscriptionId, databaseNow.AddMilliseconds(-100));
        await SetExactEligibleAfterAsync(source, justBefore.SubscriptionId, databaseNow.AddSeconds(2));
        TenantDatabaseRequest approved = await AddRetainedRequestAsync(store, source, "approved", databaseNow.AddDays(-400), 7, 30, "renewal_failed");
        await SetPurgeApprovalAsync(source, approved.SubscriptionId);
        TenantDatabaseRequest missingUid = await AddRetainedRequestAsync(store, source, "missing-uid", databaseNow.AddDays(-400), 7, 30, "renewal_failed");
        await SetClusterUidAsync(source, missingUid.SubscriptionId, null);
 
        IReadOnlyList<TenantAutomaticExpiryCandidate> candidates = await store.GetAutomaticExpiryCandidatesAsync(32, CancellationToken.None);

        Assert.Equal(new[] { standardDue.SubscriptionId, enterpriseDue.SubscriptionId, exactBoundary.SubscriptionId }.OrderBy(id => id),
            candidates.Select(candidate => candidate.SubscriptionId).OrderBy(id => id));
        Assert.Equal(standardDue.DesiredState.PaidUntilUtc!.Value.AddDays(37), candidates.Single(candidate => candidate.SubscriptionId == standardDue.SubscriptionId).EligibleAfterUtc);
        Assert.Equal(enterpriseDue.DesiredState.PaidUntilUtc!.Value.AddDays(165), candidates.Single(candidate => candidate.SubscriptionId == enterpriseDue.SubscriptionId).EligibleAfterUtc);
        var exact = Assert.Single(candidates, candidate => candidate.SubscriptionId == exactBoundary.SubscriptionId);
        Assert.Equal(databaseNow.AddMilliseconds(-100), exact.EligibleAfterUtc);
        await using NpgsqlCommand persistedRows = source.CreateCommand();
        persistedRows.CommandText = "SELECT count(*) FROM tenant_database_requests WHERE phase = 'Retained'";
        Assert.Equal(9L, (long)(await persistedRows.ExecuteScalarAsync())!);

    }


    [Fact]
    public async Task ExpiryCandidateLock_RechecksCurrentSourceOrderAndClusterUidWithoutExecutingStaleWork()
    {
        await using NpgsqlDataSource source = await CreateDatabaseAsync();
        var store = new PostgresTenantDatabaseRequestStore(source);
        DateTimeOffset databaseNow = await GetDatabaseNowAsync(source);
        TenantDatabaseRequest retained = await AddRetainedRequestAsync(store, source, "lock-fence", databaseNow.AddDays(-37), 7, 30, "renewal_failed");
        TenantAutomaticExpiryCandidate candidate = Assert.Single(await store.GetAutomaticExpiryCandidatesAsync(32, CancellationToken.None));

        bool executed = false;
        Assert.True(await store.WithCurrentAutomaticExpiryCandidateLockAsync(candidate, _ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.True(executed);

        await using (NpgsqlCommand replaceUid = source.CreateCommand())
        {
            replaceUid.CommandText = "UPDATE tenant_database_requests SET last_cluster_uid = @uid WHERE subscription_id = @subscription";
            replaceUid.Parameters.AddWithValue("uid", Guid.NewGuid());
            replaceUid.Parameters.AddWithValue("subscription", retained.SubscriptionId);
            await replaceUid.ExecuteNonQueryAsync();
        }

        executed = false;
        Assert.False(await store.WithCurrentAutomaticExpiryCandidateLockAsync(candidate, _ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.False(executed);

        var successor = retained with
        {
            DesiredState = retained.DesiredState with
            {
                SubscriptionStatus = TenantSubscriptionStatus.Active,
                SuspendedAt = null,
                PaidUntilUtc = databaseNow.AddDays(365),
            },
            ReceivedAt = databaseNow,
            SourceOrderId = Guid.NewGuid(),
            PreviousSourceOrderId = retained.SourceOrderId,
            Proof = "renewed-billing-proof",
            LastClusterUid = null,
            IsDueRetainedRenewal = false,
        };
        Assert.True(await store.UpsertAsync(successor, CancellationToken.None));

        executed = false;
        Assert.False(await store.WithCurrentAutomaticExpiryCandidateLockAsync(candidate, _ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.False(executed);
    }

    private async Task<NpgsqlDataSource> CreateDatabaseAsync()
    {
        string database = "tenant_auto_expiry_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        string connectionString = fixture.GetDatabaseConnectionString(database);
        await using (var db = new TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext(
            new DbContextOptionsBuilder<TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext>()
                .UseNpgsql(connectionString).Options))
            await db.Database.MigrateAsync();
        return NpgsqlDataSource.Create(connectionString);
    }

    private static async Task SetExactEligibleAfterAsync(NpgsqlDataSource source, Guid subscriptionId, DateTimeOffset eligibleAfter)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE tenant_database_requests SET desired_state = jsonb_set(desired_state, '{PaidUntilUtc}', to_jsonb(@paidUntil::timestamptz - make_interval(days => (desired_state->>'GracePeriodDays')::int + (desired_state->>'BackupRetentionDays')::int))) WHERE subscription_id = @subscription";
        command.Parameters.AddWithValue("paidUntil", eligibleAfter);
        command.Parameters.AddWithValue("subscription", subscriptionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<DateTimeOffset> GetDatabaseNowAsync(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT now()";
        return new DateTimeOffset(DateTime.SpecifyKind((DateTime)(await command.ExecuteScalarAsync())!, DateTimeKind.Utc));
    }

    private static async Task<TenantDatabaseRequest> AddRetainedRequestAsync(PostgresTenantDatabaseRequestStore store,
        NpgsqlDataSource source, string identifier, DateTimeOffset paidUntilUtc, int graceDays, int retentionDays, string reason)
    {
        Guid tenantId = Guid.NewGuid();
        Guid subscriptionId = Guid.NewGuid();
        Guid sourceOrderId = Guid.NewGuid();
        Guid clusterUid = Guid.NewGuid();
        DateTimeOffset receivedAt = DateTimeOffset.UtcNow;
        var desired = new TenantDatabaseDesiredState(tenantId.ToString("D"), identifier, "dedicated", "postgres", 0,
            TenantSubscriptionStatus.Active, null, PaidUntilUtc: paidUntilUtc, GracePeriodDays: graceDays,
            BackupRetentionDays: retentionDays);
        var original = new TenantDatabaseRequest(subscriptionId, desired, receivedAt, sourceOrderId, "billing-proof");
        Assert.True(await store.UpsertAsync(original, CancellationToken.None));
        await store.SaveStatusAsync(original, new(tenantId.ToString("D"), TenantDatabasePhase.Ready, EvidenceId: clusterUid),
            receivedAt, CancellationToken.None);
        Assert.True(await store.SuspendAsync(subscriptionId, sourceOrderId, tenantId, identifier, paidUntilUtc,
            reason, CancellationToken.None));

        TenantDatabaseRequest suspended = Assert.Single(await store.GetPendingAsync(32, CancellationToken.None),
            request => request.SubscriptionId == subscriptionId);
        await store.SaveStatusAsync(suspended, new(tenantId.ToString("D"), TenantDatabasePhase.Retained),
            receivedAt, CancellationToken.None);
        await using NpgsqlCommand setUidAndReleaseLease = source.CreateCommand();
        setUidAndReleaseLease.CommandText = "UPDATE tenant_database_requests SET last_cluster_uid = @uid, lease_until = now() - interval '1 second' WHERE subscription_id = @subscription";
        setUidAndReleaseLease.Parameters.AddWithValue("uid", clusterUid);
        setUidAndReleaseLease.Parameters.AddWithValue("subscription", subscriptionId);
        await setUidAndReleaseLease.ExecuteNonQueryAsync();
        return suspended with { LastClusterUid = clusterUid };
    }
    private static async Task SetPurgeApprovalAsync(NpgsqlDataSource source, Guid subscriptionId)
    {
        await using NpgsqlCommand command = source.CreateCommand();
        command.CommandText = "UPDATE tenant_database_requests SET purge_approval_id = @approval WHERE subscription_id = @subscription";
        command.Parameters.AddWithValue("approval", Guid.NewGuid());
        command.Parameters.AddWithValue("subscription", subscriptionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetClusterUidAsync(NpgsqlDataSource source, Guid subscriptionId, Guid? clusterUid)
    {
        await using NpgsqlCommand command = source.CreateCommand();
        command.CommandText = "UPDATE tenant_database_requests SET last_cluster_uid = @uid WHERE subscription_id = @subscription";
        command.Parameters.AddWithValue("uid", (object?)clusterUid ?? DBNull.Value);
        command.Parameters.AddWithValue("subscription", subscriptionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

}

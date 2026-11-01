using System.Text.Json;
using Npgsql;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>PostgreSQL-backed durable tenant request queue and status projection.</summary>
public sealed partial class PostgresTenantDatabaseRequestStore(NpgsqlDataSource dataSource) :
    ITenantDatabaseRequestStore, ITenantPurgeStore, ITenantAutomaticExpiryCandidateStore
{
    /// <summary>Reads the latest persisted status for a tenant subscription.</summary>
    public async Task<TenantDatabaseStatus?> GetStatusAsync(Guid subscriptionId, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT tenant_id, phase, detail, evidence_id FROM tenant_database_requests WHERE subscription_id = @subscription";
        command.Parameters.AddWithValue("subscription", subscriptionId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new(reader.GetGuid(0).ToString("D"), Enum.Parse<TenantDatabasePhase>(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3));
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(TenantDatabaseRequest request, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireTenantLockAsync(connection, transaction, Guid.Parse(request.DesiredState.TenantId), ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tenant_database_requests (subscription_id, tenant_id, desired_state, phase, received_at, updated_at, source_order_id, proof, previous_source_order_id)
            VALUES (@subscription, @tenant, @desired, 'Pending', @received, now(), @source_order, @proof, @previous)
            ON CONFLICT (subscription_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("subscription", request.SubscriptionId);
        command.Parameters.AddWithValue("tenant", Guid.Parse(request.DesiredState.TenantId));
        command.Parameters.Add(new NpgsqlParameter("desired", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.DesiredState) });
        command.Parameters.AddWithValue("received", request.ReceivedAt);
        if (request.SourceOrderId == Guid.Empty || string.IsNullOrWhiteSpace(request.Proof))
            throw new InvalidOperationException("A signed Billing source order is required for tenant database provisioning.");
        command.Parameters.AddWithValue("source_order", request.SourceOrderId);
        command.Parameters.AddWithValue("proof", request.Proof);
        command.Parameters.AddWithValue("previous", request.PreviousSourceOrderId);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }
        if (request.PreviousSourceOrderId != Guid.Empty)
        {
            await using NpgsqlCommand advance = connection.CreateCommand();
            advance.CommandText = """
                UPDATE tenant_database_requests
                SET source_order_id = @next, previous_source_order_id = @previous, desired_state = @desired, proof = @proof,
                    received_at = @received, updated_at = now(), phase = 'Pending', detail = NULL, lease_until = NULL,
                    evidence_id = NULL, readiness_published_at = NULL, last_cluster_uid = NULL, suspension_reason = NULL
                WHERE subscription_id = @subscription AND tenant_id = @tenant AND source_order_id = @previous
                    AND phase NOT IN ('Purging', 'Purged');
                """;
            advance.Parameters.AddWithValue("next", request.SourceOrderId);
            advance.Parameters.AddWithValue("previous", request.PreviousSourceOrderId);
            advance.Parameters.AddWithValue("subscription", request.SubscriptionId);
            advance.Parameters.AddWithValue("tenant", Guid.Parse(request.DesiredState.TenantId));
            advance.Parameters.Add(new NpgsqlParameter("desired", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.DesiredState) });
            advance.Parameters.AddWithValue("proof", request.Proof);
            advance.Parameters.AddWithValue("received", request.ReceivedAt);
            if (await advance.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return true;
            }
        }

        await using NpgsqlCommand existing = connection.CreateCommand();
        existing.CommandText = "SELECT tenant_id, desired_state::text, source_order_id, proof, previous_source_order_id, phase FROM tenant_database_requests WHERE subscription_id = @subscription";
        existing.Parameters.AddWithValue("subscription", request.SubscriptionId);
        await using (NpgsqlDataReader reader = await existing.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.GetGuid(0) != Guid.Parse(request.DesiredState.TenantId) ||
                JsonSerializer.Deserialize<TenantDatabaseDesiredState>(reader.GetString(1)) != request.DesiredState ||
                reader.GetGuid(2) != request.SourceOrderId || !string.Equals(reader.GetString(3), request.Proof, StringComparison.Ordinal) ||
                reader.GetGuid(4) != request.PreviousSourceOrderId || reader.GetString(5) is "Purging" or "Purged")
                throw new InvalidOperationException("Subscription database request differs from the paid Billing snapshot.");
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return false;
    }
    /// <inheritdoc />
    public async Task<bool> AttachEnterprisePolicyAsync(SharedKernel.Events.EnterprisePolicyAttachedIntegrationEvent transition, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireTenantLockAsync(connection, transaction, transition.TenantId, ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT tenant_id, desired_state::text, source_order_id, proof, enterprise_policy_transition_proof, phase FROM tenant_database_requests WHERE subscription_id = @subscription FOR UPDATE";
        command.Parameters.AddWithValue("subscription", transition.SubscriptionId);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.GetGuid(0) != transition.TenantId ||
                reader.GetGuid(2) != transition.SourceOrderId || reader.GetString(5) is "Purging" or "Purged")
                throw new InvalidOperationException("Policy transition does not match the current database request.");
            var desired = JsonSerializer.Deserialize<TenantDatabaseDesiredState>(reader.GetString(1))
                ?? throw new InvalidOperationException("Persisted tenant database state is invalid.");
            if (desired.Identifier != transition.TenantIdentifier)
                throw new InvalidOperationException("Policy transition tenant identifier does not match the current database request.");
            if (desired.EnterprisePolicyId is not null)
            {
                bool exact = desired.EnterprisePolicyId == transition.PolicyId && desired.EnterprisePolicyVersion == transition.PolicyVersion &&
                    desired.EnterprisePolicySourceOrderId == transition.SourceOrderId && !reader.IsDBNull(4) &&
                    reader.GetString(4) == transition.Proof;
                if (!exact) throw new InvalidOperationException("A different policy is already attached to this paid database request.");
                await reader.DisposeAsync().ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return true;
            }
            desired = desired with { GracePeriodDays = transition.GracePeriodDays, BackupRetentionDays = transition.BackupRetentionDays,
                EnterprisePolicyId = transition.PolicyId, EnterprisePolicyVersion = transition.PolicyVersion,
                EnterprisePolicySourceOrderId = transition.SourceOrderId };
            await reader.DisposeAsync().ConfigureAwait(false);
            await using NpgsqlCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE tenant_database_requests SET desired_state = @desired, enterprise_policy_transition_proof = @transition_proof, updated_at = now() WHERE subscription_id = @subscription AND tenant_id = @tenant AND source_order_id = @source_order AND enterprise_policy_transition_proof IS NULL";
            update.Parameters.Add(new NpgsqlParameter("desired", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(desired) });
            update.Parameters.AddWithValue("transition_proof", transition.Proof);
            update.Parameters.AddWithValue("subscription", transition.SubscriptionId);
            update.Parameters.AddWithValue("tenant", transition.TenantId);
            update.Parameters.AddWithValue("source_order", transition.SourceOrderId);
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Policy transition was concurrently changed.");
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SuspendAsync(Guid subscriptionId, Guid sourceOrderId, Guid tenantId, string identifier, DateTimeOffset suspendedAt, string reason, CancellationToken ct)
    {
        if (reason is not ("cancelled" or "renewal_failed"))
            throw new InvalidOperationException("A verified Billing suspension reason is required.");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireTenantLockAsync(connection, transaction, tenantId, ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tenant_database_requests
            SET desired_state = jsonb_set(jsonb_set(desired_state, '{SubscriptionStatus}', to_jsonb(@suspended_status::int)),
                    '{SuspendedAt}', to_jsonb(@suspended::timestamptz)),
                phase = 'Pending', detail = NULL, updated_at = now(), lease_until = NULL,
                suspension_reason = @reason, evidence_id = NULL, readiness_published_at = NULL
            WHERE subscription_id = @subscription AND source_order_id = @source_order AND tenant_id = @tenant
                AND desired_state->>'Identifier' = @identifier AND desired_state->>'SubscriptionStatus' = @active_status;
            """;
        command.Parameters.AddWithValue("subscription", subscriptionId);
        command.Parameters.AddWithValue("source_order", sourceOrderId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("identifier", identifier);
        command.Parameters.AddWithValue("suspended", suspendedAt);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("suspended_status", (int)TenantSubscriptionStatus.Suspended);
        command.Parameters.AddWithValue("active_status", ((int)TenantSubscriptionStatus.Active).ToString(System.Globalization.CultureInfo.InvariantCulture));
        bool changed = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return changed;
    }

    /// <inheritdoc />
    public async Task<bool> WithCurrentRequestLockAsync(TenantDatabaseRequest request, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        Guid tenantId = Guid.Parse(request.DesiredState.TenantId);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using (NpgsqlCommand acquire = connection.CreateCommand())
        {
            acquire.CommandText = "SELECT pg_advisory_lock(hashtextextended(@tenant::text, 0))";
            acquire.Parameters.AddWithValue("tenant", tenantId);
            await acquire.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        try
        {
            await using (NpgsqlCommand current = connection.CreateCommand())
            {
                current.CommandText = """
                    SELECT desired_state::text, source_order_id, proof, previous_source_order_id, phase, readiness_published_at,
                           suspension_reason, last_cluster_uid
                    FROM tenant_database_requests WHERE subscription_id = @subscription AND tenant_id = @tenant
                    """;
                current.Parameters.AddWithValue("subscription", request.SubscriptionId);
                current.Parameters.AddWithValue("tenant", tenantId);
                await using NpgsqlDataReader reader = await current.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false) ||
                    JsonSerializer.Deserialize<TenantDatabaseDesiredState>(reader.GetString(0)) != request.DesiredState ||
                    reader.GetGuid(1) != request.SourceOrderId ||
                    !string.Equals(reader.GetString(2), request.Proof, StringComparison.Ordinal) ||
                    reader.GetGuid(3) != request.PreviousSourceOrderId ||
                    (request.IsDueRetainedRenewal != (reader.GetString(4) == "Retained")) ||
                    reader.GetString(4) == "NotManaged" ||
                    (reader.GetString(4) == "Retained" && (reader.IsDBNull(6) || reader.GetString(6) != "renewal_failed" ||
                        reader.IsDBNull(7) || request.LastClusterUid != reader.GetGuid(7) ||
                        !request.IsDueRetainedRenewal || request.DesiredState.PaidUntilUtc is not { } paidUntil ||
                        request.DesiredState.GracePeriodDays < 1 ||
                        paidUntil.AddDays(request.DesiredState.GracePeriodDays) > DateTimeOffset.UtcNow)) ||
                    (reader.GetString(4) == "Ready" && !reader.IsDBNull(5)))
                    return false;
            }
            await action(ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            try
            {
                await using NpgsqlCommand release = connection.CreateCommand();
                release.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@tenant::text, 0))";
                release.Parameters.AddWithValue("tenant", tenantId);
                if (await release.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false) is not true)
                    throw new InvalidOperationException("Tenant PostgreSQL advisory lock could not be released.");
            }
            catch
            {
                NpgsqlConnection.ClearPool(connection);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantDatabaseRequest>> GetPendingAsync(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(limit));
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH candidates AS (
                SELECT subscription_id FROM tenant_database_requests
                WHERE (phase NOT IN ('Ready', 'NotManaged', 'Retained', 'Purging', 'Purged') OR
                       (phase = 'Ready' AND readiness_published_at IS NULL) OR
                       (phase = 'Retained' AND suspension_reason = 'renewal_failed' AND last_cluster_uid IS NOT NULL AND
                        (desired_state->>'PaidUntilUtc') IS NOT NULL AND
                        COALESCE((desired_state->>'GracePeriodDays')::int, -1) >= 0 AND
                        (desired_state->>'PaidUntilUtc')::timestamptz +
                            make_interval(days => (desired_state->>'GracePeriodDays')::int) <= now()))
                  AND (lease_until IS NULL OR lease_until < now())
                ORDER BY updated_at, received_at
                LIMIT @limit
                FOR UPDATE SKIP LOCKED
            )
            UPDATE tenant_database_requests AS request
            SET lease_until = now() + interval '2 minutes'
            FROM candidates
            WHERE request.subscription_id = candidates.subscription_id
            RETURNING request.subscription_id, request.desired_state::text, request.received_at, request.source_order_id,
                      request.proof, request.previous_source_order_id, request.last_cluster_uid, request.phase;
            """;
        command.Parameters.AddWithValue("limit", limit);
        var requests = new List<TenantDatabaseRequest>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                TenantDatabaseDesiredState desired = JsonSerializer.Deserialize<TenantDatabaseDesiredState>(reader.GetString(1))
                    ?? throw new InvalidOperationException("Persisted tenant desired state is invalid.");
                requests.Add(new(reader.GetGuid(0), desired, reader.GetFieldValue<DateTimeOffset>(2), reader.GetGuid(3),
                    reader.GetString(4), reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    reader.GetString(7) == "Retained"));
            }
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return requests;
    }

    /// <inheritdoc />
    public async Task SaveStatusAsync(TenantDatabaseRequest request, TenantDatabaseStatus status, DateTimeOffset reconciledAt, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tenant_database_requests
            SET phase = @phase, detail = @detail, evidence_id = @evidence,
                last_cluster_uid = CASE WHEN @phase = 'Ready' THEN @evidence ELSE last_cluster_uid END,
                updated_at = @updated, lease_until = CASE WHEN @phase = 'Retained' THEN now() + interval '2 minutes' ELSE NULL END
            WHERE subscription_id = @subscription AND source_order_id = @source_order AND tenant_id = @tenant
                AND desired_state = @desired;
            """;
        command.Parameters.AddWithValue("phase", status.Phase.ToString());
        command.Parameters.AddWithValue("detail", (object?)status.Detail ?? DBNull.Value);
        if (status.Phase == TenantDatabasePhase.Ready &&
            (status.EvidenceId is null || status.EvidenceId == Guid.Empty))
            throw new InvalidOperationException("A healthy CNPG Cluster UID is required before database readiness.");
        command.Parameters.AddWithValue("evidence", (object?)status.EvidenceId ?? DBNull.Value);
        command.Parameters.AddWithValue("updated", reconciledAt);
        command.Parameters.AddWithValue("subscription", request.SubscriptionId);
        command.Parameters.AddWithValue("source_order", request.SourceOrderId);
        command.Parameters.AddWithValue("tenant", Guid.Parse(status.TenantId));
        command.Parameters.Add(new NpgsqlParameter("desired", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.DesiredState) });
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException($"Tenant request {request.SubscriptionId} was not persisted because its source order or desired state changed.");
    }

    /// <inheritdoc />
    public async Task MarkReadinessPublishedAsync(TenantDatabaseRequest request, Guid clusterUid, DateTimeOffset publishedAt, CancellationToken ct)
    {
        if (clusterUid == Guid.Empty || publishedAt == default) throw new InvalidOperationException("Confirmed database readiness evidence is required.");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tenant_database_requests SET readiness_published_at = @published, updated_at = @published, lease_until = NULL
            WHERE subscription_id = @subscription AND source_order_id = @source_order AND tenant_id = @tenant
                AND desired_state = @desired AND phase = 'Ready' AND evidence_id = @evidence
                AND readiness_published_at IS NULL;
            """;
        command.Parameters.AddWithValue("published", publishedAt);
        command.Parameters.AddWithValue("subscription", request.SubscriptionId);
        command.Parameters.AddWithValue("source_order", request.SourceOrderId);
        command.Parameters.AddWithValue("tenant", Guid.Parse(request.DesiredState.TenantId));
        command.Parameters.Add(new NpgsqlParameter("desired", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.DesiredState) });
        command.Parameters.AddWithValue("evidence", clusterUid);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Database readiness publication could not be correlated to the current paid period.");
    }

    private static async Task AcquireTenantLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, CancellationToken ct)
    {
        await using NpgsqlCommand acquire = connection.CreateCommand();
        acquire.Transaction = transaction;
        acquire.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@tenant::text, 0))";
        acquire.Parameters.AddWithValue("tenant", tenantId);
        await acquire.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Read-only PostgreSQL selection and tenant-lock rechecks for automatic nonpayment eligibility.</summary>
public sealed partial class PostgresTenantDatabaseRequestStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantAutomaticExpiryCandidate>> GetAutomaticExpiryCandidatesAsync(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(limit));
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT subscription_id, tenant_id, desired_state->>'Identifier', source_order_id, proof,
                   previous_source_order_id, (desired_state->>'PaidUntilUtc')::timestamptz AS paid_until,
                   (desired_state->>'PaidUntilUtc')::timestamptz
                     + make_interval(days => (desired_state->>'GracePeriodDays')::int
                       + (desired_state->>'BackupRetentionDays')::int) AS eligible_after,
                   last_cluster_uid
            FROM tenant_database_requests
            WHERE phase = 'Retained' AND suspension_reason = 'renewal_failed'
                AND desired_state->>'DatabaseStrategy' = 'dedicated'
                AND desired_state->>'SubscriptionStatus' = @suspended_status
                AND desired_state->>'Identifier' IS NOT NULL
                AND source_order_id IS NOT NULL AND proof IS NOT NULL AND previous_source_order_id IS NOT NULL
                AND desired_state->>'PaidUntilUtc' IS NOT NULL
                AND desired_state->>'PaidUntilUtc' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T'
                AND desired_state->>'GracePeriodDays' ~ '^[0-9]+$'
                AND desired_state->>'BackupRetentionDays' ~ '^[0-9]+$'
                AND (desired_state->>'EnterprisePolicyId' IS NULL OR
                    (desired_state->>'EnterprisePolicyVersion' IS NOT NULL AND
                     desired_state->>'EnterprisePolicySourceOrderId' = source_order_id::text))
                AND desired_state->>'SuspendedAt' IS NOT NULL
                AND purge_approval_id IS NULL
                AND last_cluster_uid IS NOT NULL AND purge_message IS NULL
                AND (desired_state->>'PaidUntilUtc')::timestamptz
                    + make_interval(days => (desired_state->>'GracePeriodDays')::int
                      + (desired_state->>'BackupRetentionDays')::int) <= now()
            ORDER BY updated_at, received_at
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("suspended_status", ((int)TenantSubscriptionStatus.Suspended).ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("limit", limit);
        var candidates = new List<TenantAutomaticExpiryCandidate>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                candidates.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3),
                    reader.GetString(4), reader.GetGuid(5), reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetFieldValue<DateTimeOffset>(7), reader.GetGuid(8)));
            }
        }
        return candidates;
    }

    public async Task<bool> WithCurrentAutomaticExpiryCandidateLockAsync(TenantAutomaticExpiryCandidate candidate,
        Func<CancellationToken, Task> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(action);
        if (candidate.SubscriptionId == Guid.Empty || candidate.TenantId == Guid.Empty ||
            candidate.SourceOrderId == Guid.Empty || candidate.ClusterUid == Guid.Empty ||
            string.IsNullOrWhiteSpace(candidate.Identifier) || string.IsNullOrWhiteSpace(candidate.Proof))
            return false;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using (NpgsqlCommand acquire = connection.CreateCommand())
        {
            acquire.CommandText = "SELECT pg_advisory_lock(hashtextextended(@tenant::text, 0))";
            acquire.Parameters.AddWithValue("tenant", candidate.TenantId);
            await acquire.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        try
        {
            await using NpgsqlCommand current = connection.CreateCommand();
            current.CommandText = """
                SELECT desired_state::text, source_order_id, proof, previous_source_order_id, phase,
                       suspension_reason, last_cluster_uid, purge_message, purge_approval_id, now()
                FROM tenant_database_requests
                WHERE subscription_id = @subscription AND tenant_id = @tenant
                """;
            current.Parameters.AddWithValue("subscription", candidate.SubscriptionId);
            current.Parameters.AddWithValue("tenant", candidate.TenantId);
            await using (NpgsqlDataReader reader = await current.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return false;
                TenantDatabaseDesiredState? state = JsonSerializer.Deserialize<TenantDatabaseDesiredState>(reader.GetString(0));
                if (state is null || state.Identifier != candidate.Identifier || reader.GetGuid(1) != candidate.SourceOrderId ||
                    !string.Equals(reader.GetString(2), candidate.Proof, StringComparison.Ordinal) ||
                    reader.GetGuid(3) != candidate.PreviousSourceOrderId || reader.GetString(4) != "Retained" ||
                    reader.IsDBNull(5) || reader.GetString(5) != "renewal_failed" || reader.IsDBNull(6) ||
                    reader.GetGuid(6) != candidate.ClusterUid || !reader.IsDBNull(7) || !reader.IsDBNull(8) ||
                    state.SubscriptionStatus != TenantSubscriptionStatus.Suspended || state.SuspendedAt is null ||
                    state.PaidUntilUtc != candidate.PaidUntilUtc || state.GracePeriodDays < 0 || state.BackupRetentionDays < 0 ||
                    (state.EnterprisePolicyId is not null &&
                     (string.IsNullOrWhiteSpace(state.EnterprisePolicyVersion) || state.EnterprisePolicySourceOrderId != candidate.SourceOrderId)) ||
                    !string.Equals(state.DatabaseStrategy, "dedicated", StringComparison.Ordinal) ||
                    candidate.EligibleAfterUtc != candidate.PaidUntilUtc.AddDays((double)state.GracePeriodDays + state.BackupRetentionDays) ||
                    candidate.EligibleAfterUtc > reader.GetFieldValue<DateTimeOffset>(9))
                    return false;
            }
            await action(ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            try
            {
                await using NpgsqlCommand release = connection.CreateCommand();
                release.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@tenant::text, 0))";
                release.Parameters.AddWithValue("tenant", candidate.TenantId);
                await release.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                NpgsqlConnection.ClearPool(connection);
                throw;
            }
        }
    }
}

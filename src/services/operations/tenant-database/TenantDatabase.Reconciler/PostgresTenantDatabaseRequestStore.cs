using System.Text.Json;
using Npgsql;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>PostgreSQL-backed durable tenant request queue and status projection.</summary>
public sealed partial class PostgresTenantDatabaseRequestStore(NpgsqlDataSource dataSource) : ITenantDatabaseRequestStore, ITenantPurgeStore
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
                    evidence_id = NULL, readiness_published_at = NULL, last_cluster_uid = NULL
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
    public async Task<bool> SuspendAsync(Guid subscriptionId, Guid sourceOrderId, Guid tenantId, string identifier, DateTimeOffset suspendedAt, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireTenantLockAsync(connection, transaction, tenantId, ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tenant_database_requests
            SET desired_state = jsonb_set(jsonb_set(desired_state, '{SubscriptionStatus}', to_jsonb(@suspended_status::int)),
                    '{SuspendedAt}', to_jsonb(@suspended::timestamptz)),
                phase = 'Pending', detail = NULL, updated_at = now(), lease_until = NULL,
                evidence_id = NULL, readiness_published_at = NULL
            WHERE subscription_id = @subscription AND source_order_id = @source_order AND tenant_id = @tenant
                AND desired_state->>'Identifier' = @identifier AND desired_state->>'SubscriptionStatus' = @active_status;
            """;
        command.Parameters.AddWithValue("subscription", subscriptionId);
        command.Parameters.AddWithValue("source_order", sourceOrderId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("identifier", identifier);
        command.Parameters.AddWithValue("suspended", suspendedAt);
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
                    SELECT desired_state::text, source_order_id, proof, previous_source_order_id, phase, readiness_published_at
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
                    reader.GetString(4) is "NotManaged" or "Retained" ||
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
                       (phase = 'Ready' AND readiness_published_at IS NULL))
                  AND (lease_until IS NULL OR lease_until < now())
                ORDER BY updated_at, received_at
                LIMIT @limit
                FOR UPDATE SKIP LOCKED
            )
            UPDATE tenant_database_requests AS request
            SET lease_until = now() + interval '2 minutes'
            FROM candidates
            WHERE request.subscription_id = candidates.subscription_id
            RETURNING request.subscription_id, request.desired_state::text, request.received_at, request.source_order_id, request.proof, request.previous_source_order_id;
            """;
        command.Parameters.AddWithValue("limit", limit);
        var requests = new List<TenantDatabaseRequest>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                TenantDatabaseDesiredState desired = JsonSerializer.Deserialize<TenantDatabaseDesiredState>(reader.GetString(1))
                    ?? throw new InvalidOperationException("Persisted tenant desired state is invalid.");
                requests.Add(new(reader.GetGuid(0), desired, reader.GetFieldValue<DateTimeOffset>(2), reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5)));
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
                updated_at = @updated, lease_until = NULL
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

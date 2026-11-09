using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SharedKernel.Events;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Stores independently signed Customer decisions for durable asynchronous backup and deletion retries.</summary>
public sealed partial class PostgresTenantDatabaseRequestStore
{
    /// <inheritdoc />
    public async Task<bool> EnqueuePurgeApprovalAsync(TenantPurgeApprovedIntegrationEvent approval,
        TimeSpan recoveryWindow, CancellationToken ct)
    {
        if (approval.TenantId == Guid.Empty || approval.SourceOrderId == Guid.Empty ||
            approval.SubscriptionId == Guid.Empty || approval.ApprovalId == Guid.Empty ||
            recoveryWindow <= TimeSpan.Zero || approval.EligibleAtUtc < approval.SuspendedAtUtc + recoveryWindow ||
            DateTimeOffset.UtcNow < approval.EligibleAtUtc)
            throw new InvalidOperationException("An elapsed retained paid tenant approval is required.");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireTenantLockAsync(connection, transaction, approval.TenantId, ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE tenant_database_requests
            SET purge_message = @message, purge_approval_id = @approval, purge_proof = @proof, updated_at = now()
            WHERE subscription_id = @subscription AND tenant_id = @tenant AND source_order_id = @order
                AND phase = 'Retained' AND last_cluster_uid IS NOT NULL
                AND desired_state->>'TenantId' = @tenant_text
                AND desired_state->>'Identifier' = @identifier
                AND desired_state->>'SubscriptionStatus' = @suspended
                AND (desired_state->>'SuspendedAt')::timestamptz >= @suspended_at - interval '1 microsecond'
                AND (desired_state->>'SuspendedAt')::timestamptz < @suspended_at + interval '1 microsecond'
                AND now() >= @suspended_at + @recovery_window
                AND purge_approval_id IS NULL;
            """;
        command.Parameters.Add(new NpgsqlParameter("message", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(approval) });
        command.Parameters.AddWithValue("approval", approval.ApprovalId);
        command.Parameters.AddWithValue("proof", approval.Proof);
        command.Parameters.AddWithValue("subscription", approval.SubscriptionId);
        command.Parameters.AddWithValue("tenant", approval.TenantId);
        command.Parameters.AddWithValue("tenant_text", approval.TenantId.ToString("D"));
        command.Parameters.AddWithValue("order", approval.SourceOrderId);
        command.Parameters.AddWithValue("identifier", approval.TenantIdentifier);
        command.Parameters.AddWithValue("suspended", ((int)TenantSubscriptionStatus.Suspended).ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("suspended_at", approval.SuspendedAtUtc);
        command.Parameters.AddWithValue("recovery_window", recoveryWindow);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }

        await using NpgsqlCommand existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = """
            SELECT tenant_id, source_order_id, phase, purge_approval_id, purge_proof, purge_message::text
            FROM tenant_database_requests WHERE subscription_id = @subscription
            """;
        existing.Parameters.AddWithValue("subscription", approval.SubscriptionId);
        await using NpgsqlDataReader row = await existing.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await row.ReadAsync(ct).ConfigureAwait(false) || row.GetGuid(0) != approval.TenantId ||
            row.GetGuid(1) != approval.SourceOrderId || row.GetString(2) is not ("Retained" or "Purging" or "Purged") ||
            row.IsDBNull(3) || row.GetGuid(3) != approval.ApprovalId || row.IsDBNull(4) || row.GetString(4) != approval.Proof ||
            row.IsDBNull(5) || JsonSerializer.Deserialize<TenantPurgeApprovedIntegrationEvent>(row.GetString(5)) is not { } prior ||
            prior.TenantId != approval.TenantId || prior.SourceOrderId != approval.SourceOrderId ||
            prior.Proof != approval.Proof)
            throw new InvalidOperationException("Purge approval differs from the retained paid subscription or prior decision.");
        await row.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return false;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantPurgeApprovedIntegrationEvent>> GetPendingPurgeApprovalsAsync(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(limit));
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT purge_message::text FROM tenant_database_requests
            WHERE phase IN ('Retained', 'Purging') AND purge_message IS NOT NULL
            ORDER BY updated_at, received_at LIMIT @limit
            """;
        command.Parameters.AddWithValue("limit", limit);
        var pending = new List<TenantPurgeApprovedIntegrationEvent>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            pending.Add(JsonSerializer.Deserialize<TenantPurgeApprovedIntegrationEvent>(reader.GetString(0)) ??
                throw new InvalidOperationException("Persisted Customer purge approval is malformed."));
        }

        return pending;
    }
}

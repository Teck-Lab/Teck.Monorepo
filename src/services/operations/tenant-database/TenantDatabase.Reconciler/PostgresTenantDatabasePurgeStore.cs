using System.Text.Json;
using Npgsql;
using SharedKernel.Events;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Destructive work stays serialized with paid intake through the same tenant advisory lock.</summary>
public sealed partial class PostgresTenantDatabaseRequestStore
{
    /// <inheritdoc />
    public async Task<bool> WithCurrentRetainedRequestLockAsync(TenantPurgeApprovedIntegrationEvent approval,
        TimeSpan recoveryWindow, Func<TenantPurgeTarget, CancellationToken, Task> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(action);
        if (recoveryWindow <= TimeSpan.Zero || approval.TenantId == Guid.Empty || approval.SourceOrderId == Guid.Empty ||
            approval.SubscriptionId == Guid.Empty || approval.ApprovalId == Guid.Empty ||
            string.IsNullOrWhiteSpace(approval.Proof))
        {
            throw new InvalidOperationException("A signed tenant-specific paid purge approval and positive retention are required.");
        }

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using (NpgsqlCommand acquire = connection.CreateCommand())
        {
            acquire.CommandText = "SELECT pg_advisory_lock(hashtextextended(@tenant::text, 0))";
            acquire.Parameters.AddWithValue("tenant", approval.TenantId);
            await acquire.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        try
        {
            Guid clusterUid;
            TenantBackupRecoveryEvidence? recordedRecovery = null;
            await using (NpgsqlCommand current = connection.CreateCommand())
            {
                current.CommandText = """
                    SELECT tenant_id, source_order_id, desired_state::text, phase, last_cluster_uid,
                        purge_approval_id, purge_proof, purge_backup_uid, purge_restore_uid,
                        purge_backup_name, purge_verified_at
                    FROM tenant_database_requests WHERE subscription_id = @subscription
                    """;
                current.Parameters.AddWithValue("subscription", approval.SubscriptionId);
                await using NpgsqlDataReader reader = await current.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false) ||
                    reader.GetGuid(0) != approval.TenantId || reader.GetGuid(1) != approval.SourceOrderId)
                {
                    throw new InvalidOperationException("Purge approval does not match the current paid tenant period.");
                }

                TenantDatabaseDesiredState? desired = JsonSerializer.Deserialize<TenantDatabaseDesiredState>(reader.GetString(2));
                if (desired is null || desired.TenantId != approval.TenantId.ToString("D") ||
                    desired.Identifier != approval.TenantIdentifier ||
                    !string.Equals(desired.DatabaseStrategy, "dedicated", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(desired.DatabaseProvider, "postgres", StringComparison.OrdinalIgnoreCase) ||
                    desired.SubscriptionStatus != TenantSubscriptionStatus.Suspended ||
                    desired.SuspendedAt is not { } suspendedAt ||
                    Math.Abs((suspendedAt - approval.SuspendedAtUtc).Ticks) >= TimeSpan.TicksPerMicrosecond ||
                    DateTimeOffset.UtcNow < suspendedAt + recoveryWindow ||
                    approval.EligibleAtUtc < suspendedAt + recoveryWindow ||
                    reader.IsDBNull(4) || reader.GetGuid(4) == Guid.Empty)
                {
                    throw new InvalidOperationException("Purge requires the current retained and previously healthy tenant Cluster.");
                }

                string phase = reader.GetString(3);
                if (phase == "Purged")
                {
                    if (!reader.IsDBNull(5) && reader.GetGuid(5) == approval.ApprovalId &&
                        !reader.IsDBNull(6) && reader.GetString(6) == approval.Proof) return false;
                    throw new InvalidOperationException("A different approval cannot replay a completed purge.");
                }
                if (phase == "Purging" &&
                    (reader.IsDBNull(5) || reader.GetGuid(5) != approval.ApprovalId ||
                     reader.IsDBNull(6) || reader.GetString(6) != approval.Proof))
                {
                    throw new InvalidOperationException("Another approval already owns destructive reconciliation.");
                }
                if (phase is not ("Retained" or "Purging"))
                {
                    throw new InvalidOperationException("The tenant database must be retained before purge.");
                }

                clusterUid = reader.GetGuid(4);
                if (phase == "Purging" && !reader.IsDBNull(7) && !reader.IsDBNull(8) &&
                    !reader.IsDBNull(9) && !reader.IsDBNull(10))
                {
                    recordedRecovery = new(reader.GetGuid(7), reader.GetGuid(8), reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10));
                }
            }

            await action(new TenantPurgeTarget(approval.TenantId, approval.TenantIdentifier,
                approval.SourceOrderId, clusterUid, recordedRecovery), ct).ConfigureAwait(false);
            await using NpgsqlCommand complete = connection.CreateCommand();
            complete.CommandText = """
                UPDATE tenant_database_requests SET phase = 'Purged', purged_at = now(), updated_at = now()
                WHERE subscription_id = @subscription AND tenant_id = @tenant AND source_order_id = @order
                    AND phase = 'Purging' AND purge_approval_id = @approval AND purge_proof = @proof
                    AND purge_backup_uid IS NOT NULL AND purge_restore_uid IS NOT NULL
                """;
            complete.Parameters.AddWithValue("subscription", approval.SubscriptionId);
            complete.Parameters.AddWithValue("tenant", approval.TenantId);
            complete.Parameters.AddWithValue("order", approval.SourceOrderId);
            complete.Parameters.AddWithValue("approval", approval.ApprovalId);
            complete.Parameters.AddWithValue("proof", approval.Proof);
            if (await complete.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("The signed, restored backup and current paid period must be audited before completing purge.");
            }
            return true;
        }
        finally
        {
            try
            {
                await using NpgsqlCommand release = connection.CreateCommand();
                release.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@tenant::text, 0))";
                release.Parameters.AddWithValue("tenant", approval.TenantId);
                if (await release.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false) is not true)
                    throw new InvalidOperationException("Tenant purge advisory lock could not be released.");
            }
            catch
            {
                NpgsqlConnection.ClearPool(connection);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task RecordVerifiedRecoveryAsync(TenantPurgeApprovedIntegrationEvent approval,
        TenantBackupRecoveryEvidence evidence, CancellationToken ct)
    {
        if (evidence.BackupUid == Guid.Empty || evidence.RestoredClusterUid == Guid.Empty ||
            string.IsNullOrWhiteSpace(evidence.BackupName) || evidence.VerifiedAtUtc < approval.SuspendedAtUtc)
        {
            throw new InvalidOperationException("Physical recovery evidence must be complete before destructive work.");
        }

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tenant_database_requests SET phase = 'Purging', purge_approval_id = @approval,
                purge_proof = @proof, purge_backup_uid = @backup, purge_restore_uid = @restore,
                purge_backup_name = @backup_name, purge_verified_at = @verified, updated_at = now()
            WHERE subscription_id = @subscription AND tenant_id = @tenant AND source_order_id = @order
                AND phase IN ('Retained', 'Purging')
                AND (purge_approval_id IS NULL OR purge_approval_id = @approval AND purge_proof = @proof)
            """;
        command.Parameters.AddWithValue("approval", approval.ApprovalId);
        command.Parameters.AddWithValue("proof", approval.Proof);
        command.Parameters.AddWithValue("backup", evidence.BackupUid);
        command.Parameters.AddWithValue("restore", evidence.RestoredClusterUid);
        command.Parameters.AddWithValue("backup_name", evidence.BackupName);
        command.Parameters.AddWithValue("verified", evidence.VerifiedAtUtc);
        command.Parameters.AddWithValue("subscription", approval.SubscriptionId);
        command.Parameters.AddWithValue("tenant", approval.TenantId);
        command.Parameters.AddWithValue("order", approval.SourceOrderId);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Paid tenant period changed before physical recovery evidence was persisted.");
        }
    }
}

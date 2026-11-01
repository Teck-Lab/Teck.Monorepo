using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Identity of the suspended paid-period Cluster requiring a final on-demand Barman backup.</summary>
public sealed record TenantFinalBackupTarget(Guid TenantId, Guid SourceOrderId, Guid ClusterUid);

/// <summary>CNPG-confirmed completed plugin backup coordinates required for a separate recovery proof.</summary>
public sealed record TenantCompletedFinalBackup(Guid TenantId, Guid SourceOrderId, Guid ClusterUid, string BackupName,
    string BackupId, string EndWal);

/// <summary>Creates an order-fenced plugin backup and reports coordinates only after CNPG marks it completed.</summary>
public interface ITenantFinalBackupRequester
{
    /// <summary>Returns null until the requested plugin backup is completed; completion is not restore proof.</summary>
    Task<TenantCompletedFinalBackup?> RequestAsync(TenantFinalBackupTarget target, CancellationToken ct);
}

/// <summary>
/// Submits one order-fenced plugin Backup resource and writes a scoped source marker after completion.
/// </summary>
public sealed class KubernetesTenantFinalBackupRequester(HttpClient http) : ITenantFinalBackupRequester
{
    private const string Clusters = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters";
    private const string Backups = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/backups";
    private const string Plugin = "barman-cloud.cloudnative-pg.io";

    /// <summary>Submits the same final backup on retries and reports only CNPG-confirmed completion coordinates.</summary>
    public async Task<TenantCompletedFinalBackup?> RequestAsync(TenantFinalBackupTarget target, CancellationToken ct)
    {
        if (target.TenantId == Guid.Empty || target.SourceOrderId == Guid.Empty || target.ClusterUid == Guid.Empty)
            throw new InvalidOperationException("Final backup requires a tenant, paid order and Cluster UID.");

        string clusterName = "tenant-" + target.TenantId.ToString("N");
        string backupName = "tenant-final-" + target.SourceOrderId.ToString("N");
        using (HttpResponseMessage cluster = await http.GetAsync(Clusters + "/" + clusterName, ct).ConfigureAwait(false))
        {
            cluster.EnsureSuccessStatusCode();
            using JsonDocument source = await JsonDocument.ParseAsync(
                await cluster.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            JsonElement metadata = source.RootElement.GetProperty("metadata");
            if (metadata.GetProperty("uid").GetString() != target.ClusterUid.ToString("D") ||
                metadata.GetProperty("annotations").GetProperty("teck.io/source-order-id").GetString() != target.SourceOrderId.ToString("D") ||
                metadata.GetProperty("labels").GetProperty("teck.io/tenant-access").GetString() != "suspended")
                throw new InvalidOperationException("Final backup source must be the suspended Cluster of the paid order.");
        }

        string backupPath = Backups + "/" + backupName;
        using (HttpResponseMessage existing = await http.GetAsync(backupPath, ct).ConfigureAwait(false))
        {
            if (existing.StatusCode != HttpStatusCode.NotFound)
            {
                existing.EnsureSuccessStatusCode();
                using JsonDocument backup = await JsonDocument.ParseAsync(
                    await existing.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
                return VerifyBackupIdentity(backup.RootElement, target, clusterName, backupName);
            }
        }

        var resource = new
        {
            apiVersion = "postgresql.cnpg.io/v1", kind = "Backup",
            metadata = new
            {
                name = backupName, @namespace = "tenant-databases",
                annotations = new Dictionary<string, string>
                {
                    ["teck.io/source-order-id"] = target.SourceOrderId.ToString("D"),
                    ["teck.io/source-cluster-uid"] = target.ClusterUid.ToString("D"),
                },
            },
            spec = new { cluster = new { name = clusterName }, method = "plugin", pluginConfiguration = new { name = Plugin } },
        };
        using HttpResponseMessage created = await http.PostAsJsonAsync(Backups, resource, ct).ConfigureAwait(false);
        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            using HttpResponseMessage raced = await http.GetAsync(backupPath, ct).ConfigureAwait(false);
            raced.EnsureSuccessStatusCode();
            using JsonDocument backup = await JsonDocument.ParseAsync(
                await raced.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            return VerifyBackupIdentity(backup.RootElement, target, clusterName, backupName);
        }
        created.EnsureSuccessStatusCode();
        return null;
    }

    private static TenantCompletedFinalBackup? VerifyBackupIdentity(JsonElement backup, TenantFinalBackupTarget target,
        string clusterName, string backupName)
    {
        JsonElement metadata = backup.GetProperty("metadata");
        JsonElement spec = backup.GetProperty("spec");
        if (metadata.GetProperty("name").GetString() != backupName ||
            metadata.GetProperty("annotations").GetProperty("teck.io/source-order-id").GetString() != target.SourceOrderId.ToString("D") ||
            metadata.GetProperty("annotations").GetProperty("teck.io/source-cluster-uid").GetString() != target.ClusterUid.ToString("D") ||
            spec.GetProperty("cluster").GetProperty("name").GetString() != clusterName ||
            spec.GetProperty("method").GetString() != "plugin" ||
            spec.GetProperty("pluginConfiguration").GetProperty("name").GetString() != Plugin)
            throw new InvalidOperationException("Existing final backup belongs to a different tenant, order, Cluster or backup method.");

        bool hasStatus = backup.TryGetProperty("status", out JsonElement status) && status.ValueKind == JsonValueKind.Object;
        if (!hasStatus || !status.TryGetProperty("phase", out JsonElement phase) || phase.GetString() != "completed")
        {
            if (status.ValueKind == JsonValueKind.Object && status.TryGetProperty("phase", out JsonElement failedPhase) &&
                failedPhase.GetString() is "failed" or "walArchivingFailing" or "invalid backup definition")
                throw new InvalidOperationException("Final plugin backup failed and cannot be reused as recovery evidence.");
            return null;
        }

        if (!status.TryGetProperty("backupId", out JsonElement backupId) || string.IsNullOrWhiteSpace(backupId.GetString()) ||
            !status.TryGetProperty("endWal", out JsonElement endWal) || string.IsNullOrWhiteSpace(endWal.GetString()))
            throw new InvalidOperationException("Completed final plugin backup is missing backupId or endWal.");
        return new TenantCompletedFinalBackup(target.TenantId, target.SourceOrderId, target.ClusterUid,
            backupName, backupId.GetString()!, endWal.GetString()!);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Restores an independently retained CNPG snapshot and probes every tenant database before purge.</summary>
/// <param name="http">Namespace-restricted in-cluster Kubernetes API client.</param>
/// <param name="inspector">Real PostgreSQL probe of the disposable restored Cluster.</param>
/// <param name="snapshotClass">Reviewed VolumeSnapshotClass with independently retained storage.</param>
public sealed class KubernetesTenantBackupVerifier(HttpClient http, ITenantRecoveredDatabaseInspector inspector,
    string? snapshotClass) : ITenantRecoverableBackupVerifier
{
    private const string Namespace = "tenant-databases";
    private const string Clusters = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters";
    private const string Backups = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/backups";

    /// <inheritdoc />
    public async Task<TenantBackupRecoveryEvidence?> VerifyAsync(TenantPurgeTarget target, CancellationToken ct)
    {
        if (target.TenantId == Guid.Empty || target.SourceOrderId == Guid.Empty || target.ClusterUid == Guid.Empty ||
            string.IsNullOrWhiteSpace(snapshotClass) || snapshotClass.Length > 253 || snapshotClass.Contains('/'))
            throw new InvalidOperationException("A source Cluster UID and reviewed snapshot storage are required for tenant recovery.");

        string sourceName = "tenant-" + target.TenantId.ToString("N");
        string backupName = "tenant-purge-" + target.SourceOrderId.ToString("N");
        string scratchName = "restore-" + target.TenantId.ToString("N");
        using (HttpResponseMessage source = await http.GetAsync(Clusters + "/" + sourceName, ct).ConfigureAwait(false))
        {
            if (source.StatusCode == HttpStatusCode.NotFound)
            {
                if (target.RecordedRecovery is null || target.RecordedRecovery.BackupName != backupName)
                    throw new InvalidOperationException("Only an audited partial purge may resume after source Cluster deletion.");
            }
            else
            {
                source.EnsureSuccessStatusCode();
                JsonNode live = JsonNode.Parse(await source.Content.ReadAsStringAsync(ct).ConfigureAwait(false))
                    ?? throw new InvalidOperationException("Source CNPG Cluster state is unavailable.");
                if (live["metadata"]?["uid"]?.GetValue<string>() != target.ClusterUid.ToString("D") ||
                    live["metadata"]?["annotations"]?["teck.io/source-order-id"]?.GetValue<string>() != target.SourceOrderId.ToString("D") ||
                    live["metadata"]?["labels"]?["teck.io/tenant-access"]?.GetValue<string>() != "suspended" ||
                    live["spec"]?["backup"]?["volumeSnapshot"]?["className"]?.GetValue<string>() != snapshotClass ||
                    live["spec"]?["backup"]?["volumeSnapshot"]?["snapshotOwnerReference"]?.GetValue<string>() != "none")
                    throw new InvalidOperationException("A suspended source Cluster with independently retained snapshots is required.");
            }
        }

        using (HttpResponseMessage existingBackup = await http.GetAsync(Backups + "/" + backupName, ct).ConfigureAwait(false))
        {
            if (existingBackup.StatusCode == HttpStatusCode.NotFound)
            {
                if (target.RecordedRecovery is not null)
                    throw new InvalidOperationException("An independently retained backup disappeared during an interrupted purge.");
                var backup = new
                {
                    apiVersion = "postgresql.cnpg.io/v1", kind = "Backup",
                    metadata = new
                    {
                        name = backupName,
                        @namespace = Namespace,
                        annotations = new Dictionary<string, string>
                        {
                            ["teck.io/source-cluster-uid"] = target.ClusterUid.ToString("D"),
                            ["teck.io/source-order-id"] = target.SourceOrderId.ToString("D"),
                        },
                    },
                    spec = new { method = "volumeSnapshot", cluster = new { name = sourceName } },
                };
                using HttpResponseMessage created = await http.PostAsJsonAsync(Backups, backup, ct).ConfigureAwait(false);
                if (created.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
                    created.EnsureSuccessStatusCode();
                return null;
            }
            existingBackup.EnsureSuccessStatusCode();
            JsonNode stored = JsonNode.Parse(await existingBackup.Content.ReadAsStringAsync(ct).ConfigureAwait(false))
                ?? throw new InvalidOperationException("CNPG Backup state is unavailable.");
            if (stored["metadata"]?["annotations"]?["teck.io/source-cluster-uid"]?.GetValue<string>() != target.ClusterUid.ToString("D") ||
                stored["metadata"]?["annotations"]?["teck.io/source-order-id"]?.GetValue<string>() != target.SourceOrderId.ToString("D") ||
                stored["spec"]?["cluster"]?["name"]?.GetValue<string>() != sourceName ||
                stored["spec"]?["method"]?.GetValue<string>() != "volumeSnapshot" ||
                stored["metadata"]?["ownerReferences"] is JsonArray { Count: > 0 })
                throw new InvalidOperationException("CNPG Backup is not independently owned by this paid source Cluster.");
            if (stored["status"]?["phase"]?.GetValue<string>() != "completed") return null;
            if (!Guid.TryParse(stored["metadata"]?["uid"]?.GetValue<string>(), out Guid backupUid) || backupUid == Guid.Empty)
                throw new InvalidOperationException("A completed CNPG Backup UID is required.");
            if (target.RecordedRecovery is not null && target.RecordedRecovery.BackupUid != backupUid)
                throw new InvalidOperationException("The independently audited backup changed during an interrupted purge.");

            using HttpResponseMessage scratch = await http.GetAsync(Clusters + "/" + scratchName, ct).ConfigureAwait(false);
            if (scratch.StatusCode == HttpStatusCode.NotFound)
            {
                var recovered = new
                {
                    apiVersion = "postgresql.cnpg.io/v1", kind = "Cluster",
                    metadata = new
                    {
                        name = scratchName,
                        @namespace = Namespace,
                        annotations = new Dictionary<string, string>
                        {
                            ["teck.io/source-order-id"] = target.SourceOrderId.ToString("D"),
                            ["teck.io/backup-uid"] = backupUid.ToString("D"),
                        },
                    },
                    spec = new
                    {
                        instances = 1,
                        enableSuperuserAccess = true,
                        bootstrap = new
                        {
                            recovery = new
                            {
                                backup = new { name = backupName },
                                database = "tenant_" + target.TenantId.ToString("N"),
                                owner = "tenant_" + target.TenantId.ToString("N") + "_migration",
                            },
                        },
                        storage = new { size = "5Gi" },
                    },
                };
                using HttpResponseMessage created = await http.PostAsJsonAsync(Clusters, recovered, ct).ConfigureAwait(false);
                if (created.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
                    created.EnsureSuccessStatusCode();
                return null;
            }
            scratch.EnsureSuccessStatusCode();
            JsonNode restored = JsonNode.Parse(await scratch.Content.ReadAsStringAsync(ct).ConfigureAwait(false))
                ?? throw new InvalidOperationException("CNPG recovery Cluster state is unavailable.");
            if (restored["metadata"]?["annotations"]?["teck.io/source-order-id"]?.GetValue<string>() != target.SourceOrderId.ToString("D") ||
                restored["metadata"]?["annotations"]?["teck.io/backup-uid"]?.GetValue<string>() != backupUid.ToString("D"))
                throw new InvalidOperationException("A recovered Cluster belongs to a different paid source order.");
            if (restored["status"]?["phase"]?.GetValue<string>() != "Cluster in healthy state") return null;
            if (!Guid.TryParse(restored["metadata"]?["uid"]?.GetValue<string>(), out Guid scratchUid) || scratchUid == Guid.Empty ||
                !await inspector.InspectAsync(scratchName, target.TenantId, ct).ConfigureAwait(false)) return null;
            return new TenantBackupRecoveryEvidence(backupUid, scratchUid, backupName, DateTimeOffset.UtcNow);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Deletes only the retained tenant's current Cluster and five Database CRs after restored backup proof.</summary>
/// <param name="http">In-cluster Kubernetes API client confined to tenant-databases.</param>
public sealed class KubernetesTenantCnpgPurger(HttpClient http) : ITenantCnpgPurger
{
    private const string Clusters = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters/";
    private const string Databases = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/databases/";
    private const string Backups = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/backups/";

    /// <inheritdoc />
    public async Task PurgeAsync(TenantPurgeTarget target, TenantBackupRecoveryEvidence evidence, CancellationToken ct)
    {
        if (target.TenantId == Guid.Empty || target.SourceOrderId == Guid.Empty || target.ClusterUid == Guid.Empty ||
            evidence.BackupUid == Guid.Empty || evidence.RestoredClusterUid == Guid.Empty ||
            evidence.BackupName != "tenant-purge-" + target.SourceOrderId.ToString("N"))
            throw new InvalidOperationException("The paid tenant and independently restored backup must be bound before deletion.");

        string sourceName = "tenant-" + target.TenantId.ToString("N");
        string restoredName = "restore-" + target.TenantId.ToString("N");
        JsonNode backup = await RequireResourceAsync(Backups + evidence.BackupName, ct).ConfigureAwait(false);
        if (backup["metadata"]?["uid"]?.GetValue<string>() != evidence.BackupUid.ToString("D"))
            throw new InvalidOperationException("The retained backup changed before tenant deletion.");

        JsonNode? source = await ReadResourceAsync(Clusters + sourceName, ct).ConfigureAwait(false);
        if (source is not null &&
            (source["metadata"]?["uid"]?.GetValue<string>() != target.ClusterUid.ToString("D") ||
             source["metadata"]?["annotations"]?["teck.io/source-order-id"]?.GetValue<string>() != target.SourceOrderId.ToString("D") ||
             source["metadata"]?["labels"]?["teck.io/tenant-access"]?.GetValue<string>() != "suspended"))
            throw new InvalidOperationException("A replacement or recovered paid Cluster cannot be purged.");

        JsonNode? scratch = await ReadResourceAsync(Clusters + restoredName, ct).ConfigureAwait(false);
        if (scratch is not null &&
            (scratch["metadata"]?["uid"]?.GetValue<string>() != evidence.RestoredClusterUid.ToString("D") ||
             scratch["metadata"]?["annotations"]?["teck.io/backup-uid"]?.GetValue<string>() != evidence.BackupUid.ToString("D")))
            throw new InvalidOperationException("The disposable recovery Cluster does not match the verified backup.");

        var databases = new List<(string Path, JsonNode Resource)>(TenantDatabaseReconciler.SupportedServices.Count);
        foreach (string service in TenantDatabaseReconciler.SupportedServices)
        {
            string path = Databases + "tenant_" + target.TenantId.ToString("N") + "_" + service;
            JsonNode? database = await ReadResourceAsync(path, ct).ConfigureAwait(false);
            if (database is null) continue;
            if (database["spec"]?["cluster"]?["name"]?.GetValue<string>() != sourceName)
                throw new InvalidOperationException("A Database CR owned by another CNPG Cluster cannot be purged.");
            databases.Add((path, database));
        }

        // Remove the scratch recovery credentials and data before deleting the original Cluster.
        if (scratch is not null) await DeleteAndVerifyAsync(Clusters + restoredName, scratch, ct).ConfigureAwait(false);
        foreach ((string path, JsonNode database) in databases)
            await DeleteAndVerifyAsync(path, database, ct).ConfigureAwait(false);
        if (source is not null) await DeleteAndVerifyAsync(Clusters + sourceName, source, ct).ConfigureAwait(false);

        JsonNode retained = await RequireResourceAsync(Backups + evidence.BackupName, ct).ConfigureAwait(false);
        if (retained["metadata"]?["uid"]?.GetValue<string>() != evidence.BackupUid.ToString("D"))
            throw new InvalidOperationException("The independently recoverable backup disappeared during deletion.");
    }

    private async Task<JsonNode> RequireResourceAsync(string path, CancellationToken ct) =>
        await ReadResourceAsync(path, ct).ConfigureAwait(false) ??
        throw new InvalidOperationException("Required independently retained CNPG resource is unavailable.");

    private async Task<JsonNode?> ReadResourceAsync(string path, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))
            ?? throw new InvalidOperationException("CNPG resource state is empty.");
    }

    private async Task DeleteAndVerifyAsync(string path, JsonNode resource, CancellationToken ct)
    {
        if (!Guid.TryParse(resource["metadata"]?["uid"]?.GetValue<string>(), out Guid uid) || uid == Guid.Empty ||
            string.IsNullOrWhiteSpace(resource["metadata"]?["resourceVersion"]?.GetValue<string>()))
            throw new InvalidOperationException("The target CNPG resource must have a UID and resource version.");
        string version = resource["metadata"]!["resourceVersion"]!.GetValue<string>();
        using var request = new HttpRequestMessage(HttpMethod.Delete, path)
        {
            Content = JsonContent.Create(new
            {
                apiVersion = "v1", kind = "DeleteOptions",
                preconditions = new { uid = uid.ToString("D"), resourceVersion = version },
            }),
        };
        using HttpResponseMessage deleted = await http.SendAsync(request, ct).ConfigureAwait(false);
        deleted.EnsureSuccessStatusCode();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (await ReadResourceAsync(path, ct).ConfigureAwait(false) is null) return;
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException("CNPG resource deletion was not verified within the reconciliation window.");
    }
}

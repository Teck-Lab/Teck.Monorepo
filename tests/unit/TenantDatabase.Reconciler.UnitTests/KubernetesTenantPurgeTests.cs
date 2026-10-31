using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Teck.TenantDatabase.Reconciler.UnitTests;

public sealed class KubernetesTenantPurgeTests
{
    [Fact]
    public async Task VerifyAsync_BackupCompleteButRestoreUnavailable_DoesNotProvideRecoveryEvidence()
    {
        var target = new TenantPurgeTarget(Guid.NewGuid(), "retained-tenant", Guid.NewGuid(), Guid.NewGuid());
        var handler = new PurgeKubernetesHandler(target) { RestoreReady = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var verifier = new KubernetesTenantBackupVerifier(client, new RecoveredDatabaseInspector(), "snapshot-class");

        Assert.Null(await verifier.VerifyAsync(target, CancellationToken.None));
        Assert.Equal(0, handler.OriginalDeleteCalls);
        Assert.Contains(handler.SeenPaths, path => path.Contains("/backups/tenant-purge-", StringComparison.Ordinal));
        Assert.Contains(handler.SeenPaths, path => path.EndsWith("/clusters", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_CreatesRecoveryClusterWhoseSnapshotJobNameFitsKubernetesDnsLabel()
    {
        var target = new TenantPurgeTarget(Guid.NewGuid(), "retained-tenant", Guid.NewGuid(), Guid.NewGuid());
        var handler = new PurgeKubernetesHandler(target);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var verifier = new KubernetesTenantBackupVerifier(client, new RecoveredDatabaseInspector(), "snapshot-class");

        Assert.Null(await verifier.VerifyAsync(target, CancellationToken.None));
        Assert.NotNull(handler.CreatedRecoveryClusterName);
        Assert.True((handler.CreatedRecoveryClusterName + "-1-snapshot-recovery").Length <= 63,
            "CNPG snapshot recovery Job hostname and label must fit the Kubernetes DNS label limit.");
    }

    [Fact]
    public async Task VerifyAsync_RejectsWrongSourceClusterUidBeforeBackupOrRestore()
    {
        var target = new TenantPurgeTarget(Guid.NewGuid(), "retained-tenant", Guid.NewGuid(), Guid.NewGuid());
        var handler = new PurgeKubernetesHandler(target) { WrongSourceUid = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var verifier = new KubernetesTenantBackupVerifier(client, new RecoveredDatabaseInspector(), "snapshot-class");

        await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync(target, CancellationToken.None));
        Assert.Equal(0, handler.OriginalDeleteCalls);
        Assert.DoesNotContain(handler.SeenPaths, path => path.EndsWith("/backups", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_WhenScratchClusterIsHealthyButDatabasesNotQueryable_RejectsBackup()
    {
        var target = new TenantPurgeTarget(Guid.NewGuid(), "retained-tenant", Guid.NewGuid(), Guid.NewGuid());
        var handler = new PurgeKubernetesHandler(target) { RestoreReady = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var inspector = new RecoveredDatabaseInspector();
        var verifier = new KubernetesTenantBackupVerifier(client, inspector, "snapshot-class");

        Assert.Null(await verifier.VerifyAsync(target, CancellationToken.None));
        Assert.Equal(1, inspector.Calls);
        inspector.DatabasesReady = true;
        var recovered = await verifier.VerifyAsync(target, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.NotEqual(Guid.Empty, recovered.BackupUid);
        Assert.NotEqual(Guid.Empty, recovered.RestoredClusterUid);
        Assert.Equal(2, inspector.Calls);
    }

    [Fact]
    public async Task PurgeAsync_DeletesOnlyFencedTenantResourcesAndKeepsRecoverableBackup()
    {
        var target = new TenantPurgeTarget(Guid.NewGuid(), "retained-tenant", Guid.NewGuid(), Guid.NewGuid());
        var evidence = new TenantBackupRecoveryEvidence(Guid.NewGuid(), Guid.NewGuid(),
            "tenant-purge-" + target.SourceOrderId.ToString("N"), DateTimeOffset.UtcNow);
        var handler = new DeletingPurgeHandler(target, evidence);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };

        await new KubernetesTenantCnpgPurger(client).PurgeAsync(target, evidence, CancellationToken.None);

        Assert.Equal(7, handler.Deleted.Count);
        Assert.All(handler.Deleted, entry =>
        {
            Assert.StartsWith("/apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/", entry.Path, StringComparison.Ordinal);
            using var body = JsonDocument.Parse(entry.Body);
            Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("preconditions").GetProperty("uid").GetString()));
        });
        Assert.Contains(handler.Deleted, entry => entry.Path.EndsWith($"/clusters/tenant-{target.TenantId:N}", StringComparison.Ordinal));
        Assert.Contains(handler.Deleted, entry => entry.Path.EndsWith($"/clusters/restore-{target.TenantId:N}", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Deleted, entry => entry.Path.Contains("/backups/", StringComparison.Ordinal));
    }

    private sealed class DeletingPurgeHandler(TenantPurgeTarget target, TenantBackupRecoveryEvidence evidence) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Deleted { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Delete)
            {
                Deleted.Add((path, await request.Content!.ReadAsStringAsync(ct)));
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (request.Method != HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            if (Deleted.Any(entry => entry.Path == path)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/" + evidence.BackupName, StringComparison.Ordinal))
                return Json(new { metadata = new { uid = evidence.BackupUid.ToString("D") } });
            if (path.EndsWith($"/clusters/restore-{target.TenantId:N}", StringComparison.Ordinal))
                return Json(new { metadata = new { uid = evidence.RestoredClusterUid.ToString("D"), resourceVersion = "7",
                    annotations = new Dictionary<string, string> { ["teck.io/backup-uid"] = evidence.BackupUid.ToString("D") } } });
            if (path.EndsWith($"/clusters/tenant-{target.TenantId:N}", StringComparison.Ordinal))
                return Json(new { metadata = new { uid = target.ClusterUid.ToString("D"), resourceVersion = "8",
                    annotations = new Dictionary<string, string> { ["teck.io/source-order-id"] = target.SourceOrderId.ToString("D") },
                    labels = new Dictionary<string, string> { ["teck.io/tenant-access"] = "suspended" } } });
            if (path.Contains("/databases/", StringComparison.Ordinal))
                return Json(new { metadata = new { uid = Guid.NewGuid().ToString("D"), resourceVersion = "5" },
                    spec = new { cluster = new { name = "tenant-" + target.TenantId.ToString("N") } } });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value)),
        };
    }

    private sealed class RecoveredDatabaseInspector : ITenantRecoveredDatabaseInspector
    {
        public bool DatabasesReady { get; set; }
        public int Calls { get; private set; }
        public Task<bool> InspectAsync(string clusterName, Guid tenantId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(DatabasesReady);
        }
    }

    private sealed class PurgeKubernetesHandler(TenantPurgeTarget target) : HttpMessageHandler
    {
        public bool WrongSourceUid { get; set; }
        public bool RestoreReady { get; set; }
        public int OriginalDeleteCalls { get; private set; }
        public List<string> SeenPaths { get; } = [];
        public string? CreatedRecoveryClusterName { get; private set; }
        private Guid backupUid = Guid.NewGuid();
        private Guid scratchUid = Guid.NewGuid();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            SeenPaths.Add(path);
            string name = $"tenant-{target.TenantId:N}";
            if (request.Method == HttpMethod.Get && path.EndsWith("/clusters/" + name, StringComparison.Ordinal))
                return Json(new
                {
                    metadata = new { uid = WrongSourceUid ? Guid.NewGuid().ToString("D") : target.ClusterUid.ToString("D"),
                        annotations = new Dictionary<string, string> { ["teck.io/source-order-id"] = target.SourceOrderId.ToString("D") },
                        labels = new Dictionary<string, string> { ["teck.io/tenant-access"] = "suspended" } },
                    spec = new { backup = new { volumeSnapshot = new { className = "snapshot-class", snapshotOwnerReference = "none" } } },
                });
            if (request.Method == HttpMethod.Get && path.Contains("/backups/", StringComparison.Ordinal))
                return Json(new
                {
                    metadata = new { uid = backupUid.ToString("D"), annotations = new Dictionary<string, string>
                    {
                        ["teck.io/source-cluster-uid"] = target.ClusterUid.ToString("D"),
                        ["teck.io/source-order-id"] = target.SourceOrderId.ToString("D"),
                    } }, spec = new { method = "volumeSnapshot", cluster = new { name } },
                    status = new { phase = "completed" },
                });
            if (request.Method == HttpMethod.Get && path.EndsWith($"/clusters/restore-{target.TenantId:N}", StringComparison.Ordinal))
                 return RestoreReady ? Json(new { metadata = new { uid = scratchUid.ToString("D"), annotations = new Dictionary<string, string>
                     { ["teck.io/source-order-id"] = target.SourceOrderId.ToString("D"), ["teck.io/backup-uid"] = backupUid.ToString("D") } },
                     status = new { phase = "Cluster in healthy state" } })
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post && path.EndsWith("/clusters", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                CreatedRecoveryClusterName = body.RootElement.GetProperty("metadata").GetProperty("name").GetString();
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/backups", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Created);
            if (request.Method == HttpMethod.Delete && path.EndsWith("/clusters/" + name, StringComparison.Ordinal)) OriginalDeleteCalls++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value)),
        };
    }
}

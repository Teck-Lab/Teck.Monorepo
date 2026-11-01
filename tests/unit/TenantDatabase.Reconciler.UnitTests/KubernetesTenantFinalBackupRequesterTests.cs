using System.Net;
using System.Text.Json;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Teck.TenantDatabase.Reconciler.UnitTests;

public sealed class KubernetesTenantFinalBackupRequesterTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Order = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid ClusterUid = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private const string ClusterName = "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa";
    private const string BackupName = "tenant-final-bbbbbbbbbbbb4bbb8bbbbbbbbbbbbbbb";

    [Theory]
    [InlineData("running", null, null, false)]
    [InlineData("completed", "backup-42", "000000010000000000000042", true)]
    public async Task RequestAsync_ReturnsEvidenceOnlyForCompletedMatchingBackupWithCoordinates(string phase,
        string? backupId, string? endWal, bool expectedComplete)
    {
        using var handler = new KubernetesHandler(Cluster(ClusterUid, Order, "suspended"),
            Backup(ClusterUid, Order, phase, backupId, endWal));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var requester = new KubernetesTenantFinalBackupRequester(client);

        TenantCompletedFinalBackup? result = await requester.RequestAsync(new(Tenant, Order, ClusterUid), TestContext.Current.CancellationToken);

        Assert.Equal(expectedComplete, result is not null);
        if (expectedComplete)
        {
            Assert.Equal("backup-42", result!.BackupId);
            Assert.Equal("000000010000000000000042", result.EndWal);
        }
    }


    [Fact]
    public async Task RequestAsync_CreatesPluginBackupBoundToSuspendedClusterAndPaidOrder()
    {
        using var handler = new KubernetesHandler(Cluster(ClusterUid, Order, "suspended"), null);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var requester = new KubernetesTenantFinalBackupRequester(client);

        await requester.RequestAsync(new(Tenant, Order, ClusterUid), TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, item => item.Method == "POST");
        Assert.Equal("/apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/backups", request.Path);
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal(BackupName, payload.RootElement.GetProperty("metadata").GetProperty("name").GetString());
        Assert.Equal(Order.ToString("D"), payload.RootElement.GetProperty("metadata").GetProperty("annotations").GetProperty("teck.io/source-order-id").GetString());
        Assert.Equal(ClusterUid.ToString("D"), payload.RootElement.GetProperty("metadata").GetProperty("annotations").GetProperty("teck.io/source-cluster-uid").GetString());
        var spec = payload.RootElement.GetProperty("spec");
        Assert.Equal(ClusterName, spec.GetProperty("cluster").GetProperty("name").GetString());
        Assert.Equal("plugin", spec.GetProperty("method").GetString());
        Assert.Equal("barman-cloud.cloudnative-pg.io", spec.GetProperty("pluginConfiguration").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("active", false)]
    [InlineData("suspended", true)]
    public async Task RequestAsync_RejectsMissingOrUnfencedSourceBeforeCreatingBackup(string access, bool wrongUid)
    {
        using var handler = new KubernetesHandler(Cluster(wrongUid ? Guid.NewGuid() : ClusterUid, Order, access), null);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var requester = new KubernetesTenantFinalBackupRequester(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => requester.RequestAsync(new(Tenant, Order, ClusterUid), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requests, item => item.Method == "POST");
    }

    [Fact]
    public async Task RequestAsync_ReplayWithDifferentBackupOwnerFailsClosed()
    {
        using var handler = new KubernetesHandler(Cluster(ClusterUid, Order, "suspended"), Backup(Guid.NewGuid(), Order));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var requester = new KubernetesTenantFinalBackupRequester(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => requester.RequestAsync(new(Tenant, Order, ClusterUid), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requests, item => item.Method == "POST");
    }

    [Fact]
    public async Task RequestAsync_ReplayOfMatchingBackupIsIdempotent()
    {
        using var handler = new KubernetesHandler(Cluster(ClusterUid, Order, "suspended"), Backup(ClusterUid, Order));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var requester = new KubernetesTenantFinalBackupRequester(client);

        await requester.RequestAsync(new(Tenant, Order, ClusterUid), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(handler.Requests, item => item.Method == "POST");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("walArchivingFailing")]
    [InlineData("invalid backup definition")]
    public async Task RequestAsync_ReplayOfFailedBackupCannotBeTreatedAsAnActiveFinalBackup(string phase)
    {
        string failedBackup = Backup(ClusterUid, Order, phase);
        using var handler = new KubernetesHandler(Cluster(ClusterUid, Order, "suspended"), failedBackup);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var requester = new KubernetesTenantFinalBackupRequester(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => requester.RequestAsync(
            new(Tenant, Order, ClusterUid), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requests, item => item.Method == "POST");
    }

    private static string Cluster(Guid uid, Guid order, string access) => JsonSerializer.Serialize(new
    {
        metadata = new { uid, annotations = new Dictionary<string, string> { ["teck.io/source-order-id"] = order.ToString("D") },
            labels = new Dictionary<string, string> { ["teck.io/tenant-access"] = access } },
    });

    private static string Backup(Guid uid, Guid order, string? phase = null, string? backupId = null, string? endWal = null) => JsonSerializer.Serialize(new
    {
        metadata = new { name = BackupName, annotations = new Dictionary<string, string>
        {
            ["teck.io/source-order-id"] = order.ToString("D"), ["teck.io/source-cluster-uid"] = uid.ToString("D"),
        } },
        spec = new { cluster = new { name = ClusterName }, method = "plugin", pluginConfiguration = new { name = "barman-cloud.cloudnative-pg.io" } },
        status = phase is null ? null : new { phase, backupId, endWal },
    });


    private sealed class KubernetesHandler(string source, string? backup) : HttpMessageHandler
    {
        public List<(string Method, string Path, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method.Method, path, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (request.Method == HttpMethod.Get && path.EndsWith("/clusters/" + ClusterName, StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent(source) };
            if (request.Method == HttpMethod.Get && path.EndsWith("/backups/" + BackupName, StringComparison.Ordinal))
                return backup is null ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new StringContent(backup) };
            if (request.Method == HttpMethod.Post && path.EndsWith("/backups", StringComparison.Ordinal))
                return new(HttpStatusCode.Created) { Content = new StringContent("{}") };
            throw new InvalidOperationException("Unexpected Kubernetes request: " + request.Method + " " + path);
        }
    }
}

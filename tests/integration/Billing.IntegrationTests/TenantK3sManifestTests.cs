using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Teck.TenantDatabase.Reconciler;
using Testcontainers.K3s;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Validates tenant CNPG and Barman manifests against version-pinned CRDs in a real disposable Kubernetes API.</summary>
public sealed class TenantK3sManifestTests
{
    private const string Namespace = "tenant-databases";
    private const string ClusterName = "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa";
    private const string K3sImage = "rancher/k3s:v1.30.6-k3s1";

    [Fact]
    public async Task PaidTenantResources_AreAcceptedByPinnedCnpgAndBarmanApis()
    {
        await using var k3s = new K3sBuilder(K3sImage)
            .WithCommand("server", "--disable=traefik", "--disable-agent")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(K3sBuilder.KubeSecurePort)
                .UntilCommandIsCompleted("test -s /etc/rancher/k3s/k3s.yaml"))
            .Build();
        await k3s.StartAsync(TestContext.Current.CancellationToken);
        using var config = new TemporaryKubeconfig(await k3s.GetKubeconfigAsync());
        await ApplyPinnedCrdsAsync(config.Path, TestContext.Current.CancellationToken);

        using var client = CreateKubernetesClient(config.Path);
        using var namespaceRequest = JsonContent.Create(new { apiVersion = "v1", kind = "Namespace", metadata = new { name = Namespace } });
        using (var response = await client.PostAsync("api/v1/namespaces", namespaceRequest, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Do not install the operator in this API/schema test: a separate operator+object-store
        // test must establish actual WAL archiving, recovery, hibernation and full erasure.
        var paidOrder = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new UnexpectedOpenBao(),
            new TenantBarmanStoreOptions("s3://tenant-backups/", "https://s3.example.test", "barman-s3"));
        await backend.ApplyClusterAsync(new(Namespace, ClusterName, 3, "tenant-secret", paidOrder,
            HasScheduledBackups: true, HasHighAvailability: true), TestContext.Current.CancellationToken);

        await AssertResourceAsync(client, "apis/barmancloud.cnpg.io/v1/namespaces/tenant-databases/objectstores/" + ClusterName,
            "barmancloud.cnpg.io/v1", TestContext.Current.CancellationToken);
        await AssertResourceAsync(client, "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters/" + ClusterName,
            "postgresql.cnpg.io/v1", TestContext.Current.CancellationToken);
        await AssertResourceAsync(client, "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/scheduledbackups/" + ClusterName,
            "postgresql.cnpg.io/v1", TestContext.Current.CancellationToken);

        using (var hibernationPatch = JsonContent.Create(new object[]
        {
            new { op = "add", path = "/metadata/annotations/cnpg.io~1hibernation", value = "on" },
        }))
        {
            hibernationPatch.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json-patch+json");
            using var hibernated = await client.PatchAsync("apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters/" + ClusterName,
                hibernationPatch, TestContext.Current.CancellationToken);
            hibernated.EnsureSuccessStatusCode();
        }
        var successor = Guid.NewGuid();
        await backend.ApplyClusterAsync(new(Namespace, ClusterName, 3, "tenant-secret", successor, paidOrder,
            HasScheduledBackups: true, HasHighAvailability: true), TestContext.Current.CancellationToken);
        using (var renewed = await client.GetAsync("apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters/" + ClusterName,
            TestContext.Current.CancellationToken))
        {
            renewed.EnsureSuccessStatusCode();
            using var cluster = JsonDocument.Parse(await renewed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("off", cluster.RootElement.GetProperty("metadata").GetProperty("annotations").GetProperty("cnpg.io/hibernation").GetString());
            Assert.Equal(successor.ToString("D"), cluster.RootElement.GetProperty("metadata").GetProperty("annotations").GetProperty("teck.io/source-order-id").GetString());
        }

        var tenantId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var clusterUid = Guid.NewGuid();
        using var finalBackup = JsonContent.Create(new
        {
            apiVersion = "postgresql.cnpg.io/v1", kind = "Backup",
            metadata = new { name = "tenant-final-" + paidOrder.ToString("N"),
                annotations = new Dictionary<string, string>
                {
                    ["teck.io/source-order-id"] = paidOrder.ToString("D"),
                    ["teck.io/source-cluster-uid"] = clusterUid.ToString("D"),
                } },
            spec = new { cluster = new { name = "tenant-" + tenantId.ToString("N") }, method = "plugin",
                pluginConfiguration = new { name = "barman-cloud.cloudnative-pg.io" } },
        });
        using var finalBackupResponse = await client.PostAsync(
            "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/backups", finalBackup, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, finalBackupResponse.StatusCode);
        using var backup = JsonDocument.Parse(await finalBackupResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("plugin", backup.RootElement.GetProperty("spec").GetProperty("method").GetString());
        Assert.Equal("barman-cloud.cloudnative-pg.io", backup.RootElement.GetProperty("spec")
            .GetProperty("pluginConfiguration").GetProperty("name").GetString());
    }

    private static async Task AssertResourceAsync(HttpClient client, string path, string apiVersion, CancellationToken ct)
    {
        using HttpResponseMessage response = await client.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        Assert.Equal(apiVersion, body.RootElement.GetProperty("apiVersion").GetString());
        Assert.Equal(ClusterName, body.RootElement.GetProperty("metadata").GetProperty("name").GetString());
    }

    private static HttpClient CreateKubernetesClient(string kubeconfigPath)
    {
        var kubeconfig = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("kubectl")
        {
            ArgumentList = { "--kubeconfig", kubeconfigPath, "config", "view", "--minify", "--raw", "-o", "json" },
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        using (kubeconfig)
        {
            string raw = kubeconfig.StandardOutput.ReadToEnd();
            if (kubeconfig.WaitForExit(10_000) is false || kubeconfig.ExitCode != 0)
                throw new InvalidOperationException("Cannot read disposable Kubernetes credentials.");
            using JsonDocument config = JsonDocument.Parse(raw);
            JsonElement cluster = config.RootElement.GetProperty("clusters")[0].GetProperty("cluster");
            JsonElement user = config.RootElement.GetProperty("users")[0].GetProperty("user");
            var ca = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(cluster.GetProperty("certificate-authority-data").GetString()!));
            var certificate = X509Certificate2.CreateFromPem(
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(user.GetProperty("client-certificate-data").GetString()!)),
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(user.GetProperty("client-key-data").GetString()!)));
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificate);
            handler.ServerCertificateCustomValidationCallback = (_, _, chain, errors) =>
            {
                if (chain is null || errors is not (System.Net.Security.SslPolicyErrors.None or
                    System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)) return false;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(ca);
                return chain.Build(chain.ChainElements[0].Certificate);
            };
            var client = new HttpClient(handler) { BaseAddress = new Uri(cluster.GetProperty("server").GetString()!) };
            return client;
        }
    }

    private static async Task ApplyPinnedCrdsAsync(string kubeconfigPath, CancellationToken ct)
    {
        foreach (string file in new[] { "postgresql.cnpg.io_clusters.yaml", "postgresql.cnpg.io_databases.yaml",
            "postgresql.cnpg.io_scheduledbackups.yaml", "postgresql.cnpg.io_backups.yaml",
            "barmancloud.cnpg.io_objectstores.yaml" })
        {
            var start = new System.Diagnostics.ProcessStartInfo("kubectl")
            {
                ArgumentList = { "--kubeconfig", kubeconfigPath, "apply", "--server-side", "-f", Path.Combine(AppContext.BaseDirectory, "KubernetesCrds", file) },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var apply = System.Diagnostics.Process.Start(start)!;
            Task<string> output = apply.StandardOutput.ReadToEndAsync(ct);
            Task<string> errors = apply.StandardError.ReadToEndAsync(ct);
            await apply.WaitForExitAsync(ct);
            Assert.True(apply.ExitCode == 0, $"Pinned CRD {file} failed: {await errors} {await output}");
        }
    }

    private sealed class TemporaryKubeconfig : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "teck-k3s-" + Guid.NewGuid().ToString("N"));
        public TemporaryKubeconfig(string kubeconfig) => File.WriteAllText(Path, kubeconfig);
        public void Dispose() => File.Delete(Path);
    }

    private sealed class UnexpectedRoleRevoker : ITenantDatabaseRoleRevoker
    {
        public Task RevokeAsync(Guid tenantId, CancellationToken ct) => throw new InvalidOperationException("Unexpected revocation.");
    }

    private sealed class UnexpectedOpenBao : ITenantOpenBaoConnectionStore
    {
        public Task WriteAsync(string path, Guid order, Guid previous, string value, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected credential publication.");
        public Task RevokeAsync(string path, Guid order, CancellationToken ct) => throw new InvalidOperationException("Unexpected revocation.");
    }
}

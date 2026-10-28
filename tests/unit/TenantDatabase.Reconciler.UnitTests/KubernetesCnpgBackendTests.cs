using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Teck.TenantDatabase.Reconciler.UnitTests;

public sealed class KubernetesCnpgBackendTests
{
    [Fact]
    public async Task ApplyClusterAsync_UsesTenantNamespaceAndGeneratedResourceName()
    {
        var handler = new RecordingHandler(missingOnGet: true);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client);

        await backend.ApplyClusterAsync(new("tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", 3, "tenant-secret"), TestContext.Current.CancellationToken);
        var createdSecrets = handler.Requests.Where(request => request.Method == "POST" &&
            request.Path == "/api/v1/namespaces/tenant-databases/secrets").ToArray();
        Assert.Equal(11, createdSecrets.Length);
        foreach (var secret in createdSecrets)
        {
            using var body = JsonDocument.Parse(secret.Body);
            Assert.Equal("kubernetes.io/basic-auth", body.RootElement.GetProperty("type").GetString());
            Assert.NotEmpty(Convert.FromBase64String(body.RootElement.GetProperty("data").GetProperty("username").GetString()!));
            Assert.True(Convert.FromBase64String(body.RootElement.GetProperty("data").GetProperty("password").GetString()!).Length >= 32);
        }

        Assert.Equal("POST", handler.Method);
        Assert.Equal("/apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters", handler.Path);
        Assert.Contains("\"namespace\":\"tenant-databases\"", handler.Body);
        Assert.Contains("\"instances\":3", handler.Body);
        using var manifest = JsonDocument.Parse(handler.Body);
        var roles = manifest.RootElement.GetProperty("spec").GetProperty("managed").GetProperty("roles")
            .EnumerateArray().ToArray();
        Assert.Equal(10, roles.Length);
        foreach (var service in new[] { "basket", "catalog", "inventory", "order", "pricing" })
        {
            Assert.Contains(roles, role => role.GetProperty("name").GetString()!.EndsWith("_" + service + "_migration", StringComparison.Ordinal));
            Assert.Contains(roles, role => role.GetProperty("name").GetString()!.EndsWith("_" + service + "_app", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ApplyClusterAsync_MergePatchesExistingClusterWithoutReplacingOperatorMetadata()
    {
        var handler = new RecordingHandler("{\"metadata\":{\"resourceVersion\":\"12\",\"labels\":{\"cnpg.io/managed\":\"true\"}}}", missingSecretsOnGet: true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client);

        await backend.ApplyClusterAsync(new("tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", 3, "tenant-secret"), TestContext.Current.CancellationToken);

        Assert.Equal("PATCH", handler.Method);
        Assert.Equal("/apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters/tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", handler.Path);
        Assert.Contains("\"instances\":3", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyClusterAsync_RejectsTheArgoOwnedDevelopmentNamespace()
    {
        var handler = new RecordingHandler();
        var backend = new KubernetesCnpgBackend(new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") });

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.ApplyClusterAsync(new("postgres", "postgres", 1, "ignored"), TestContext.Current.CancellationToken));
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task SuspendAccessAsync_PreservesTheFirstSuspensionTimeAcrossRetries()
    {
        var suspendedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var initialSuspension = suspendedAt.ToString("O");
        var originalSecret = JsonSerializer.Serialize(new
        {
            metadata = new { labels = new Dictionary<string, string> { ["teck.io/suspended-at"] = initialSuspension } },
            data = new Dictionary<string, string>(),
        });
        var handler = new RecordingHandler(originalSecret);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client);

        await backend.SuspendAccessAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", suspendedAt, TestContext.Current.CancellationToken);

        Assert.Equal("PATCH", handler.Method);
        using var patch = JsonDocument.Parse(handler.Body);
        Assert.Equal(initialSuspension, patch.RootElement.GetProperty("metadata").GetProperty("labels")
            .GetProperty("teck.io/suspended-at").GetString());
        // JSON merge patch with data: {} preserves existing username/password; it is not revocation.
        Assert.False(patch.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
            data.EnumerateObject().Any());
    }

    [Fact]
    public async Task SuspendAccessAsync_FailsClosedWhenExistingDatabaseCredentialsRemainUsable()
    {
        const string existingSecret = """
            {"metadata":{"labels":{}},"data":{"username":"dGVzdA==","password":"c2VjcmV0"}}
            """;
        var handler = new RecordingHandler(existingSecret);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.SuspendAccessAsync(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplicationCredentials_RequireActualUsernamePasswordInsteadOfOnlyExistingSecret()
    {
        var handler = new RecordingHandler("{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client);

        Assert.False(await backend.HasApplicationSecretAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"status\":{\"phase\":\"Cluster in healthy state\"}}", false)]
    [InlineData("{\"metadata\":{\"uid\":\"\"},\"status\":{\"phase\":\"Cluster in healthy state\"}}", false)]
    [InlineData("{\"metadata\":{\"uid\":\"d0d58a9c-6c2b-4da3-bec0-01fd82ef7251\"},\"status\":{\"phase\":\"Cluster in healthy state\"}}", true)]
    public async Task IsClusterReadyAsync_RequiresKubernetesResourceUidAndHealthyPhase(string payload, bool expected)
    {
        var handler = new RecordingHandler(payload);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client);

        Assert.Equal(expected, await backend.IsClusterReadyAsync("tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa",
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"metadata\":{\"generation\":2},\"status\":{\"applied\":true,\"observedGeneration\":2}}", true)]
    [InlineData("{\"metadata\":{\"generation\":2},\"status\":{\"applied\":true,\"observedGeneration\":1}}", false)]
    [InlineData("{\"metadata\":{\"generation\":2},\"status\":{\"applied\":false,\"observedGeneration\":2}}", false)]
    [InlineData("{\"metadata\":{\"generation\":2},\"status\":{\"conditions\":[{\"type\":\"Ready\",\"status\":\"True\"}]}}", false)]
    [InlineData("{\"status\":{}}", false)]
    public async Task IsDatabaseReadyAsync_RequiresAppliedCurrentGeneration(string payload, bool expected)
    {
        var handler = new RecordingHandler(payload);
        var backend = new KubernetesCnpgBackend(new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") });

        bool result = await backend.IsDatabaseReadyAsync("tenant-databases", "tenant_db", TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        Assert.Equal("/apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/databases/tenant_db", handler.Path);
    }

    private sealed class RecordingHandler(string responseBody = "{}", bool missingOnGet = false, bool missingSecretsOnGet = false) : HttpMessageHandler
    {
        public string? Method { get; private set; }
        public string? Path { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public List<(string Method, string Path, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method.Method;
            Path = request.RequestUri?.PathAndQuery;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((Method, Path ?? string.Empty, Body));
            if ((missingOnGet || (missingSecretsOnGet && Path!.StartsWith("/api/v1/namespaces/tenant-databases/secrets/", StringComparison.Ordinal))) && request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}

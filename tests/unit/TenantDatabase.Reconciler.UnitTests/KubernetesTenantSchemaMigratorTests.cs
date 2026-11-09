using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Teck.TenantDatabase.Reconciler.UnitTests;

public sealed class KubernetesTenantSchemaMigratorTests
{
    [Fact]
    public async Task PaidPeriod_UsesServiceOwnedImageAndSecret_OnlyCompletedMatchingJobPermitsReadiness()
    {
        var transport = new JobTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://kubernetes.test/") };
        var connections = new TestConnections();
        var migrator = new KubernetesTenantSchemaMigrator(http, connections,
            new Dictionary<string, string> { ["basket"] = "registry.example/basket-api@sha256:1234" }, "teck", "https://sso.example");
        var tenantId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        var state = new TenantDatabaseDesiredState(tenantId, "paid-business", "dedicated", "postgres", 0,
            TenantSubscriptionStatus.Active, null);
        var order = Guid.NewGuid();

        await Assert.ThrowsAsync<TenantMigrationPendingException>(() => migrator.MigrateAsync(state, order, Guid.Empty, "basket",
            TestContext.Current.CancellationToken));
        var submitted = Assert.Single(transport.Created);
        using var job = JsonDocument.Parse(submitted);
        JsonElement root = job.RootElement;
        Assert.Equal("tenant-databases", root.GetProperty("metadata").GetProperty("namespace").GetString());
        Assert.Equal(order.ToString("D"), root.GetProperty("metadata").GetProperty("annotations").GetProperty("teck.io/source-order-id").GetString());
        JsonElement pod = root.GetProperty("spec").GetProperty("template").GetProperty("spec");
        Assert.Equal("Never", pod.GetProperty("restartPolicy").GetString());
        JsonElement container = Assert.Single(pod.GetProperty("containers").EnumerateArray());
        Assert.Equal("registry.example/basket-api@sha256:1234", container.GetProperty("image").GetString());
        Assert.Equal("--migrate", Assert.Single(container.GetProperty("args").EnumerateArray()).GetString());
        JsonElement[] environment = container.GetProperty("env").EnumerateArray().ToArray();
        JsonElement connection = Assert.Single(environment, entry => entry.GetProperty("name").GetString() == "ConnectionStrings__Default");
        Assert.Equal("ConnectionStrings__Default", connection.GetProperty("name").GetString());
        Assert.Equal("tenant-migration-connection", connection.GetProperty("valueFrom").GetProperty("secretKeyRef").GetProperty("name").GetString());
        Assert.Contains(environment, entry => entry.GetProperty("name").GetString() == "Keycloak__realm" && entry.GetProperty("value").GetString() == "teck");
        Assert.Contains(environment, entry => entry.GetProperty("name").GetString() == "Keycloak__auth-server-url" && entry.GetProperty("value").GetString() == "https://sso.example");
        Assert.Contains(environment, entry => entry.GetProperty("name").GetString() == "Keycloak__resource" && entry.GetProperty("value").GetString() == "basket-api");
        Assert.DoesNotContain("Password=", submitted, StringComparison.OrdinalIgnoreCase);

        transport.Completed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync(state, order, Guid.Empty, "basket",
            TestContext.Current.CancellationToken));
        connections.AppConnectionReady = true;
        await migrator.MigrateAsync(state, order, Guid.Empty, "basket", TestContext.Current.CancellationToken);
        Assert.Single(transport.Created);
        await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync(state, Guid.NewGuid(), order, "basket",
            TestContext.Current.CancellationToken));
    }

    private sealed class TestConnections : ITenantMigrationConnectionProvider
    {
        public bool AppConnectionReady { get; set; }
        public Task<string> EnsureMigrationConnectionAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, string service, CancellationToken ct)
            => Task.FromResult("tenant-migration-connection");
        public Task VerifyAndPublishApplicationConnectionAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, Guid previousSourceOrderId, string service, CancellationToken ct)
            => AppConnectionReady ? Task.CompletedTask : throw new InvalidOperationException("Application role cannot log in.");
    }

    private sealed class JobTransport : HttpMessageHandler
    {
        public List<string> Created { get; } = [];
        public bool Completed { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                Created.Add(await request.Content!.ReadAsStringAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent("{}") };
            }
            if (Created.Count == 0) return new HttpResponseMessage(HttpStatusCode.NotFound);
            var job = JsonNode.Parse(Created[0])!;
            job["metadata"]!["uid"] = Guid.NewGuid().ToString("D");
            job["status"] = JsonSerializer.SerializeToNode(new
            {
                succeeded = Completed ? 1 : 0,
                conditions = Completed ? new[] { new { type = "Complete", status = "True" } } : Array.Empty<object>(),
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(job.ToJsonString()) };
        }
        private static StringContent JsonContent(string body) => new(body, Encoding.UTF8, "application/json");
    }
}

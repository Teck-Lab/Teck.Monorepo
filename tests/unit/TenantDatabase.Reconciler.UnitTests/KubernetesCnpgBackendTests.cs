using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        await backend.ApplyClusterAsync(new("tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", 3, "tenant-secret",
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1")), TestContext.Current.CancellationToken);
        var createdSecrets = handler.Requests.Where(request => request.Method == "POST" &&
            request.Path == "/api/v1/namespaces/tenant-databases/secrets").ToArray();
        Assert.Equal(12, createdSecrets.Length);
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
        Assert.Equal(11, roles.Length);
        JsonElement manager = Assert.Single(roles, role => role.GetProperty("name").GetString() == "tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_manager");
        Assert.True(manager.GetProperty("createrole").GetBoolean());
        Assert.Contains(manager.GetProperty("inRoles").EnumerateArray(), role => role.GetString() == "pg_signal_backend");
        Assert.Contains(manager.GetProperty("inRoles").EnumerateArray(), role =>
            role.GetString() == "tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_migration");
        Assert.Equal(12, manager.GetProperty("inRoles").GetArrayLength());
        Assert.Equal("tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa-manager",
            manager.GetProperty("passwordSecret").GetProperty("name").GetString());
        var bootstrapSql = manifest.RootElement.GetProperty("spec").GetProperty("bootstrap").GetProperty("initdb")
            .GetProperty("postInitSQL").EnumerateArray().Select(sql => sql.GetString()).ToArray();
        Assert.Contains("CREATE ROLE \"tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_manager\" NOLOGIN NOINHERIT CREATEROLE", bootstrapSql);
        Assert.Contains("GRANT \"tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_migration\" TO \"tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_manager\" WITH ADMIN TRUE, INHERIT FALSE, SET FALSE", bootstrapSql);
        Assert.Contains("GRANT pg_signal_backend TO \"tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_manager\" WITH ADMIN FALSE, INHERIT TRUE, SET FALSE", bootstrapSql);
        foreach (var service in new[] { "basket", "catalog", "inventory", "order", "pricing" })
        {
            Assert.Contains(roles, role => role.GetProperty("name").GetString()!.EndsWith("_" + service + "_migration", StringComparison.Ordinal));
            Assert.Contains(roles, role => role.GetProperty("name").GetString()!.EndsWith("_" + service + "_app", StringComparison.Ordinal));
            foreach (string purpose in new[] { "migration", "app" })
            {
                string role = $"tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_{service}_{purpose}";
                Assert.Contains($"CREATE ROLE \"{role}\" NOLOGIN", bootstrapSql);
                Assert.Contains(manager.GetProperty("inRoles").EnumerateArray(), member => member.GetString() == role);
                Assert.Contains($"GRANT \"{role}\" TO \"tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_manager\" WITH ADMIN TRUE, INHERIT FALSE, SET FALSE", bootstrapSql);
            }
        }
    }

    [Fact]
    public async Task ApplyClusterAsync_ConditionallyPatchesExistingClusterWithoutReplacingOperatorMetadata()
    {
        var handler = new RecordingHandler("{\"metadata\":{\"resourceVersion\":\"12\",\"annotations\":{\"teck.io/source-order-id\":\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1\"},\"labels\":{\"cnpg.io/managed\":\"true\"}}}", missingSecretsOnGet: true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        await backend.ApplyClusterAsync(new("tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", 3, "tenant-secret",
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1")), TestContext.Current.CancellationToken);

        Assert.Equal("PATCH", handler.Method);
        Assert.Equal("/apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters/tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", handler.Path);
        using var patch = JsonDocument.Parse(handler.Body);
        var operations = patch.RootElement.EnumerateArray().ToArray();
        Assert.Contains(operations, op => op.GetProperty("op").GetString() == "test" &&
            op.GetProperty("path").GetString() == "/metadata/resourceVersion" &&
            op.GetProperty("value").GetString() == "12");
        Assert.Contains(operations, op => op.GetProperty("path").GetString() == "/spec/instances" &&
            op.GetProperty("value").GetInt32() == 3);
        Assert.DoesNotContain(operations, op => op.GetProperty("path").GetString() == "/metadata/labels");
    }

    [Fact]
    public async Task ApplyClusterAsync_RejectsTheArgoOwnedDevelopmentNamespace()
    {
        var handler = new RecordingHandler();
        var backend = new KubernetesCnpgBackend(new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") }, new UnexpectedRoleRevoker(), new RecordingOpenBao());

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
        var handler = new RecordingHandler(originalSecret, missingClustersOnGet: true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        await backend.SuspendAccessAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business", Guid.NewGuid(), suspendedAt, TestContext.Current.CancellationToken);

        Assert.Equal("PATCH", handler.Method);
        using var patch = JsonDocument.Parse(handler.Body);
        Assert.Equal(initialSuspension, patch.RootElement.GetProperty("metadata").GetProperty("labels")
            .GetProperty("teck.io/suspended-at").GetString());
        // Empty orphaned Secrets may retain the first suspension timestamp, but cannot grant access.
        Assert.False(patch.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
            data.EnumerateObject().Any());
    }

    [Fact]
    public async Task SuspendAccessAsync_WhenOperatorRolesAreStillUsable_DoesNotReportRetained()
    {
        const string cluster = """
            {"metadata":{"uid":"d0d58a9c-6c2b-4da3-bec0-01fd82ef7251"},
             "spec":{"managed":{"roles":[{"name":"tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_basket_app","login":true}]}},
             "status":{"managedRolesStatus":{"byStatus":{"pending-reconciliation":["tenant_aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa_basket_app"],"reconciled":[]}}}}
            """;
        var handler = new RecordingHandler(cluster);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.SuspendAccessAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business", Guid.NewGuid(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuspendAccessAsync_AcknowledgesOnlyAfterOperatorAndPostgresRevokeThenFiveVaultPaths()
    {
        Guid sourceOrder = Guid.NewGuid();
        var handler = new ConvergedClusterHandler(sourceOrder);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var revoker = new FailingOnceRoleRevoker();
        var openBao = new RecordingOpenBao();
        var backend = new KubernetesCnpgBackend(client, revoker, openBao);
        var suspendedAt = DateTimeOffset.UtcNow.AddDays(-2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.SuspendAccessAsync(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business", sourceOrder, suspendedAt, CancellationToken.None));
        Assert.Empty(openBao.Revoked);
        Assert.Equal(1, revoker.Calls);
        await backend.SuspendAccessAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business",
            sourceOrder, suspendedAt.AddDays(1), CancellationToken.None);
        Assert.Equal(2, revoker.Calls);
        Assert.Equal(new[] { "basket", "catalog", "inventory", "order", "pricing" },
            openBao.Revoked.Select(path => path.Split('/')[2]).ToArray());
        Assert.All(openBao.Revoked, path => Assert.StartsWith("tenants/paid-business/", path, StringComparison.Ordinal));
        Assert.Equal(suspendedAt.ToString("O"), handler.FirstSuspension);
        Assert.All(handler.ManagedLogins, login => Assert.False(login));
        Assert.All(handler.ManagedServiceRoles, role =>
        {
            Assert.True(role["disablePassword"]?.GetValue<bool>());
            Assert.Null(role["passwordSecret"]);
        });
    }

    [Fact]
    public async Task ApplyClusterAsync_AfterSuspension_RejectsOldSourceOrderBeforeCreatingAnyCredentials()
    {
        Guid firstOrder = Guid.NewGuid();
        var handler = new ConvergedClusterHandler(firstOrder);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new FailingOnceRoleRevoker(failFirst: false), new RecordingOpenBao());
        var tenant = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        var when = DateTimeOffset.UtcNow;
        await backend.SuspendAccessAsync(tenant, "paid-business", firstOrder, when, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.ApplyClusterAsync(
            new("tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", 2,
                "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa-credentials", firstOrder), CancellationToken.None));
        Assert.Equal(0, handler.CreatedSecrets);
        Assert.All(handler.ManagedLogins, login => Assert.False(login));
    }

    [Fact]
    public async Task ApplyClusterAsync_SignedSuccessorReopensOnlyCurrentOrderAndClearsPriorSuspension()
    {
        Guid first = Guid.NewGuid(), next = Guid.NewGuid();
        var handler = new ConvergedClusterHandler(first);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new FailingOnceRoleRevoker(failFirst: false), new RecordingOpenBao());
        var initial = new TenantClusterSpec("tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", 2,
            "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa-credentials", first);
        await backend.ApplyClusterAsync(initial, CancellationToken.None);
        Assert.Equal(12, handler.CreatedSecrets);
        foreach (string service in new[] { "basket", "catalog", "inventory", "order", "pricing" })
            handler.SeedMigrationSecret("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", first, service);
        await backend.SuspendAccessAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business", first,
            DateTimeOffset.UtcNow, CancellationToken.None);
        foreach (string service in new[] { "basket", "catalog", "inventory", "order", "pricing" })
        {
            foreach (string purpose in new[] { "migration", "app" })
                Assert.False(handler.HasSecret($"tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa-{service}-{purpose}-{first:N}"));
            Assert.False(handler.HasMigrationSecret("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", first, service));
        }
        Assert.True(handler.HasSecret("tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa-manager"));
        var successor = new TenantClusterSpec("tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", 2,
            "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa-credentials", next, first);

        await backend.ApplyClusterAsync(successor, CancellationToken.None);

        Assert.Equal(next.ToString("D"), handler.CurrentOrder);
        Assert.Equal("active", handler.AccessState);
        Assert.Null(handler.FirstSuspension);
        Assert.All(handler.ManagedLogins, Assert.True);
        Assert.Equal(22, handler.CreatedSecrets);
        foreach (string service in new[] { "basket", "catalog", "inventory", "order", "pricing" })
            foreach (string purpose in new[] { "migration", "app" })
                Assert.Equal($"tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa-{service}-{purpose}-{next:N}",
                    handler.RoleSecretName(service, purpose));
        await backend.ApplyClusterAsync(successor, CancellationToken.None);
    }

    [Fact]
    public async Task SuspendAccessAsync_StalePaidPeriodCannotDisableSuccessorRolesOrVaultCredentials()
    {
        Guid first = Guid.NewGuid(), next = Guid.NewGuid();
        var handler = new ConvergedClusterHandler(next);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var roles = new FailingOnceRoleRevoker(failFirst: false);
        var openBao = new RecordingOpenBao();
        var backend = new KubernetesCnpgBackend(client, roles, openBao);

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.SuspendAccessAsync(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business", first, DateTimeOffset.UtcNow,
            CancellationToken.None));
        Assert.Equal(0, roles.Calls);
        Assert.Empty(openBao.Revoked);
        Assert.All(handler.ManagedLogins, Assert.True);
    }

    [Fact]
    public async Task ApplyClusterAsync_ConcurrentSuspensionRejectsStaleResourceVersionBeforeReopeningLogins()
    {
        Guid sourceOrder = Guid.NewGuid();
        var handler = new ConvergedClusterHandler(sourceOrder) { SuspendDuringNextActivePatch = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        await Assert.ThrowsAsync<HttpRequestException>(() => backend.ApplyClusterAsync(new(
            "tenant-databases", "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa", 2,
            "tenant-aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa-credentials", sourceOrder), CancellationToken.None));
        Assert.Equal("suspended", handler.AccessState);
        Assert.All(handler.ManagedLogins, login => Assert.False(login));
    }

    [Fact]
    public async Task SuspendAccessAsync_ConcurrentSuccessorRejectsStaleResourceVersionBeforeDisablingLogins()
    {
        Guid first = Guid.NewGuid(), next = Guid.NewGuid();
        var handler = new ConvergedClusterHandler(first) { RenewDuringNextSuspendPatch = next };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var roles = new FailingOnceRoleRevoker(failFirst: false);
        var backend = new KubernetesCnpgBackend(client, roles, new RecordingOpenBao());

        await Assert.ThrowsAsync<HttpRequestException>(() => backend.SuspendAccessAsync(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business", first, DateTimeOffset.UtcNow,
            CancellationToken.None));
        Assert.Equal(next.ToString("D"), handler.CurrentOrder);
        Assert.All(handler.ManagedLogins, Assert.True);
        Assert.Equal(0, roles.Calls);
    }

    [Fact]
    public async Task SuspendAccessAsync_FailsClosedWhenExistingDatabaseCredentialsRemainUsable()
    {
        const string existingSecret = """
            {"metadata":{"labels":{}},"data":{"username":"dGVzdA==","password":"c2VjcmV0"}}
            """;
        var handler = new RecordingHandler(existingSecret, missingClustersOnGet: true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.SuspendAccessAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business", Guid.NewGuid(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuspendAccessAsync_FailsClosedWhenBootstrapSecretIsGoneButServiceRoleCredentialsRemain()
    {
        const string roleSecret = """
            {"type":"kubernetes.io/basic-auth","data":{"username":"dGVzdA==","password":"c2VjcmV0"}}
            """;
        var handler = new RecordingHandler(roleSecret, missingBootstrapOnGet: true, missingClustersOnGet: true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.SuspendAccessAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "paid-business", Guid.NewGuid(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplicationCredentials_RequireActualUsernamePasswordInsteadOfOnlyExistingSecret()
    {
        var handler = new RecordingHandler("{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        Assert.False(await backend.HasApplicationSecretAsync("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"status\":{\"phase\":\"Cluster in healthy state\"}}", false)]
    [InlineData("{\"metadata\":{\"uid\":\"\"},\"status\":{\"phase\":\"Cluster in healthy state\"}}", false)]
    [InlineData("{\"metadata\":{\"uid\":\"d0d58a9c-6c2b-4da3-bec0-01fd82ef7251\"},\"status\":{\"phase\":\"Cluster in healthy state\"}}", true)]
    public async Task IsClusterReadyAsync_RequiresKubernetesResourceUidAndHealthyPhase(string payload, bool expected)
    {
        var handler = new RecordingHandler(payload);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var backend = new KubernetesCnpgBackend(client, new UnexpectedRoleRevoker(), new RecordingOpenBao());

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
        var backend = new KubernetesCnpgBackend(new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") }, new UnexpectedRoleRevoker(), new RecordingOpenBao());

        bool result = await backend.IsDatabaseReadyAsync("tenant-databases", "tenant_db", TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        Assert.Equal("/apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/databases/tenant_db", handler.Path);
    }

    private sealed class FailingOnceRoleRevoker(bool failFirst = true) : ITenantDatabaseRoleRevoker
    {
        public int Calls { get; private set; }
        public Task RevokeAsync(Guid tenantId, CancellationToken ct)
        {
            Calls++;
            if (failFirst && Calls == 1) throw new InvalidOperationException("PostgreSQL has not terminated all sessions.");
            return Task.CompletedTask;
        }
    }

    private sealed class ConvergedClusterHandler : HttpMessageHandler
    {
        private readonly JsonObject cluster;
        private readonly Dictionary<string, string> secrets = new(StringComparer.Ordinal);
        public bool HasSecret(string name) => secrets.ContainsKey(name);
        private static string MigrationName(string tenant, Guid order, string service) => "tenant-migration-" + service + "-" +
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(tenant + order.ToString("D") + service)).AsSpan(0, 16));
        public void SeedMigrationSecret(string tenant, Guid order, string service) =>
            secrets.Add(MigrationName(tenant, order, service), "{\"kind\":\"Secret\"}");
        public bool HasMigrationSecret(string tenant, Guid order, string service) =>
            secrets.ContainsKey(MigrationName(tenant, order, service));
        public bool SuspendDuringNextActivePatch { get; set; }
        public Guid RenewDuringNextSuspendPatch { get; set; }
        public int CreatedSecrets { get; private set; }
        public string? CurrentOrder => cluster["metadata"]?["annotations"]?["teck.io/source-order-id"]?.GetValue<string>();
        public string? AccessState => cluster["metadata"]?["labels"]?["teck.io/tenant-access"]?.GetValue<string>();
        public string? FirstSuspension => cluster["metadata"]?["labels"]?["teck.io/suspended-at"]?.GetValue<string>();
        public IEnumerable<bool> ManagedLogins => ((JsonArray)cluster["spec"]!["managed"]!["roles"]!)
            .Select(role => role!["login"]!.GetValue<bool>()).Skip(1);
        public IEnumerable<JsonNode> ManagedServiceRoles => ((JsonArray)cluster["spec"]!["managed"]!["roles"]!)
            .Skip(1).Select(role => role!);
        public string RoleSecretName(string service, string purpose)
        {
            var role = ((JsonArray)cluster["spec"]!["managed"]!["roles"]!).Single(item =>
                item!["name"]!.GetValue<string>().EndsWith($"_{service}_{purpose}", StringComparison.Ordinal));
            return role!["passwordSecret"]!["name"]!.GetValue<string>();
        }

        public ConvergedClusterHandler(Guid sourceOrderId = default)
        {
            const string suffix = "aaaaaaaaaaaa4aaa8aaaaaaaaaaaaaaa";
            string[] roles = ["tenant_" + suffix + "_manager", .. new[] { "basket", "catalog", "inventory", "order", "pricing" }
                .SelectMany(service => new[] { $"tenant_{suffix}_{service}_migration", $"tenant_{suffix}_{service}_app" })];
            cluster = new JsonObject
            {
                ["metadata"] = new JsonObject { ["uid"] = "d0d58a9c-6c2b-4da3-bec0-01fd82ef7251", ["resourceVersion"] = "1",
                    ["annotations"] = new JsonObject { ["teck.io/source-order-id"] = sourceOrderId.ToString("D") },
                    ["labels"] = new JsonObject { ["teck.io/tenant-access"] = "active" } },
                ["spec"] = new JsonObject { ["managed"] = new JsonObject { ["roles"] = new JsonArray(roles.Select(role =>
                    (JsonNode?)new JsonObject { ["name"] = role, ["login"] = true }).ToArray()) } },
                ["status"] = new JsonObject { ["managedRolesStatus"] = new JsonObject { ["byStatus"] = new JsonObject
                {
                    ["reconciled"] = new JsonArray(roles.Select(role => (JsonNode?)JsonValue.Create(role)).ToArray()),
                } } },
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("/secrets/", StringComparison.Ordinal))
            {
                string name = request.RequestUri.AbsolutePath.Split('/')[^1];
                return secrets.TryGetValue(name, out string? body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Delete && request.RequestUri!.AbsolutePath.Contains("/secrets/", StringComparison.Ordinal))
            {
                string name = request.RequestUri.AbsolutePath.Split('/')[^1];
                return new HttpResponseMessage(secrets.Remove(name) ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/secrets", StringComparison.Ordinal))
            {
                string body = await request.Content!.ReadAsStringAsync(ct);
                using var secret = JsonDocument.Parse(body);
                string name = secret.RootElement.GetProperty("metadata").GetProperty("name").GetString()!;
                if (!secrets.TryAdd(name, body)) return new HttpResponseMessage(HttpStatusCode.Conflict);
                CreatedSecrets++;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Patch)
            {
                var patch = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                if (SuspendDuringNextActivePatch)
                {
                    SuspendDuringNextActivePatch = false;
                    cluster["metadata"]!["resourceVersion"] = "2";
                    cluster["metadata"]!["labels"] = new JsonObject { ["teck.io/tenant-access"] = "suspended" };
                    foreach (JsonNode? role in (JsonArray)cluster["spec"]!["managed"]!["roles"]!)
                        if (role!["name"]!.GetValue<string>().EndsWith("_manager", StringComparison.Ordinal) is false)
                            role["login"] = false;
                }
                if (RenewDuringNextSuspendPatch != Guid.Empty)
                {
                    cluster["metadata"]!["resourceVersion"] = "2";
                    cluster["metadata"]!["annotations"]!["teck.io/source-order-id"] = RenewDuringNextSuspendPatch.ToString("D");
                    RenewDuringNextSuspendPatch = Guid.Empty;
                }
                if (patch is JsonArray operations)
                {
                    if (operations.Any(op => op?["op"]?.GetValue<string>() == "test" &&
                        ((op["path"]?.GetValue<string>() == "/metadata/resourceVersion" &&
                          op["value"]?.GetValue<string>() != cluster["metadata"]?["resourceVersion"]?.GetValue<string>()) ||
                         (op["path"]?.GetValue<string>() == "/metadata/annotations/teck.io~1source-order-id" &&
                          op["value"]?.GetValue<string>() != cluster["metadata"]?["annotations"]?["teck.io/source-order-id"]?.GetValue<string>()))))
                        return new HttpResponseMessage(HttpStatusCode.Conflict);
                    foreach (JsonNode? operation in operations)
                    {
                        string path = operation!["path"]!.GetValue<string>();
                        JsonNode? value = operation["value"];
                        switch (path)
                        {
                            case "/metadata/annotations/teck.io~1source-order-id":
                                cluster["metadata"]!["annotations"]!["teck.io/source-order-id"] = value!.DeepClone();
                                break;
                            case "/metadata/labels/teck.io~1tenant-access":
                                var labels = cluster["metadata"]!["labels"] as JsonObject ?? new JsonObject();
                                labels["teck.io/tenant-access"] = value!.DeepClone();
                                cluster["metadata"]!["labels"] = labels.DeepClone();
                                break;
                            case "/metadata/labels/teck.io~1suspended-at":
                                if (operation["op"]?.GetValue<string>() == "remove")
                                    ((JsonObject)cluster["metadata"]!["labels"]!).Remove("teck.io/suspended-at");
                                else
                                    cluster["metadata"]!["labels"]!["teck.io/suspended-at"] = value!.DeepClone();
                                break;
                            case "/spec/managed/roles":
                                cluster["spec"]!["managed"]!["roles"] = value!.DeepClone();
                                break;
                        }
                    }
                }
                else
                {
                    if (patch["metadata"]?["annotations"] is JsonNode annotations)
                        cluster["metadata"]!["annotations"] = annotations.DeepClone();
                    cluster["spec"]!["managed"]!["roles"] = patch["spec"]!["managed"]!["roles"]!.DeepClone();
                    if (patch["metadata"]?["labels"] is JsonObject labels)
                    {
                        var current = cluster["metadata"]!["labels"] as JsonObject ?? new JsonObject();
                        foreach (var label in labels)
                        {
                            if (label.Value is null) current.Remove(label.Key);
                            else current[label.Key] = label.Value.DeepClone();
                        }
                        cluster["metadata"]!["labels"] = current.DeepClone();
                    }
                }
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(cluster.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class UnexpectedRoleRevoker : ITenantDatabaseRoleRevoker
    {
        public Task RevokeAsync(Guid tenantId, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected role revocation without settled CNPG state.");
    }

    private sealed class RecordingOpenBao : ITenantOpenBaoConnectionStore
    {
        public List<string> Revoked { get; } = [];
        public Task WriteAsync(string path, Guid sourceOrderId, Guid previousSourceOrderId, string write, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected active credential publication during suspension.");
        public Task RevokeAsync(string path, Guid sourceOrderId, CancellationToken ct)
        {
            Revoked.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler(string responseBody = "{}", bool missingOnGet = false, bool missingSecretsOnGet = false, bool missingBootstrapOnGet = false, bool missingClustersOnGet = false) : HttpMessageHandler
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
            if ((missingOnGet || (missingSecretsOnGet && Path!.StartsWith("/api/v1/namespaces/tenant-databases/secrets/", StringComparison.Ordinal)) ||
                (missingBootstrapOnGet && Path!.EndsWith("-credentials", StringComparison.Ordinal)) ||
                (missingClustersOnGet && Path!.StartsWith("/apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters/", StringComparison.Ordinal))) && request.Method == HttpMethod.Get)
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

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SharedKernel.Events;

namespace Teck.TenantDatabase.Reconciler;

public enum TenantSubscriptionStatus { Pending, Active, Suspended, Cancelled }
public enum TenantDatabasePhase { Pending, NotManaged, Provisioning, AwaitingCredentials, Migrating, Ready, Retained, Purging, Purged }
public sealed record TenantDatabaseDesiredState(string TenantId, string Identifier, string DatabaseStrategy, string DatabaseProvider, int ReadReplicas, TenantSubscriptionStatus SubscriptionStatus, DateTimeOffset? SuspendedAt);
public sealed record TenantDatabaseStatus(string TenantId, TenantDatabasePhase Phase, string? Detail = null, Guid? EvidenceId = null);
public sealed record TenantClusterSpec(string Namespace, string Name, int Instances, string SecretName,
    Guid SourceOrderId = default, Guid PreviousSourceOrderId = default);
public sealed record TenantDatabaseSpec(string Namespace, string Name, string ClusterName, string Owner);

/// <summary>Persisted, replay-safe request sourced from a trusted subscription snapshot.</summary>
public sealed record TenantDatabaseRequest(Guid SubscriptionId, TenantDatabaseDesiredState DesiredState, DateTimeOffset ReceivedAt, Guid SourceOrderId = default, string Proof = "", Guid PreviousSourceOrderId = default);

/// <summary>Durable queue and status store for accepted tenant database requests.</summary>
public interface ITenantDatabaseRequestStore
{
    Task<bool> UpsertAsync(TenantDatabaseRequest request, CancellationToken ct);
    Task<bool> SuspendAsync(Guid subscriptionId, Guid sourceOrderId, Guid tenantId, string identifier, DateTimeOffset suspendedAt, CancellationToken ct);
    Task<IReadOnlyList<TenantDatabaseRequest>> GetPendingAsync(int limit, CancellationToken ct);
    /// <summary>Runs external reconciliation only while this paid request is current and blocks newer intake until it finishes.</summary>
    Task<bool> WithCurrentRequestLockAsync(TenantDatabaseRequest request, Func<CancellationToken, Task> action, CancellationToken ct);
    Task SaveStatusAsync(TenantDatabaseRequest request, TenantDatabaseStatus status, DateTimeOffset reconciledAt, CancellationToken ct);
    Task MarkReadinessPublishedAsync(TenantDatabaseRequest request, Guid clusterUid, DateTimeOffset publishedAt, CancellationToken ct);
}

/// <summary>Publishes a signed database-readiness integration event through the configured transport.</summary>
public interface ITenantDatabaseReadinessPublisher
{
    Task PublishAsync(TenantProvisioningReadyIntegrationEvent readiness, CancellationToken ct);
}

/// <summary>Executes durable requests until each request reaches an externally observable state.</summary>
public sealed class TenantDatabaseWorker(ITenantDatabaseRequestStore store, TenantDatabaseReconciler reconciler,
    ITenantDatabaseReadinessPublisher publisher, DatabaseReadinessSigningKey signingKey,
    ILogger<TenantDatabaseWorker> logger)
{
    /// <summary>Reconciles the next persisted batch and stores every resulting phase.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantDatabaseRequest> requests = await store.GetPendingAsync(32, ct).ConfigureAwait(false);
        foreach (TenantDatabaseRequest request in requests)
        {
            try
            {
                await store.WithCurrentRequestLockAsync(request, async lockedCt =>
                {
                    TenantDatabaseStatus status = await reconciler.ReconcileAsync(request.DesiredState, lockedCt,
                        request.SourceOrderId, request.PreviousSourceOrderId).ConfigureAwait(false);
                    await store.SaveStatusAsync(request, status, DateTimeOffset.UtcNow, lockedCt).ConfigureAwait(false);
                    if (status.Phase == TenantDatabasePhase.Ready && status.EvidenceId is { } evidence && evidence != Guid.Empty)
                    {
                        var readiness = new TenantProvisioningReadyIntegrationEvent
                        {
                            TenantId = Guid.Parse(request.DesiredState.TenantId), SourceOrderId = request.SourceOrderId,
                            SubscriptionId = request.SubscriptionId, Provisioner = "database", EvidenceId = evidence,
                        };
                        readiness.Proof = signingKey.Sign(readiness);
                        await publisher.PublishAsync(readiness, lockedCt).ConfigureAwait(false);
                        await store.MarkReadinessPublishedAsync(request, evidence, DateTimeOffset.UtcNow, lockedCt).ConfigureAwait(false);
                    }
                    logger.LogInformation("Tenant database request {SubscriptionId} for {TenantId} reached {Phase}", request.SubscriptionId, status.TenantId, status.Phase);
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogError(exception, "Tenant database request {SubscriptionId} failed; it remains leased for retry.", request.SubscriptionId); }
        }
        return requests.Count;
    }
}

public interface ITenantCnpgBackend
{
    Task ApplyClusterAsync(TenantClusterSpec spec, CancellationToken ct);
    Task<bool> HasApplicationSecretAsync(string tenantId, Guid sourceOrderId, CancellationToken ct);
    Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct);
    Task<Guid?> GetReadyClusterUidAsync(string ns, string name, CancellationToken ct);
    Task ApplyDatabaseAsync(TenantDatabaseSpec spec, CancellationToken ct);
    Task<bool> IsDatabaseReadyAsync(string ns, string name, CancellationToken ct);
    Task SuspendAccessAsync(string tenantId, string identifier, Guid sourceOrderId, DateTimeOffset suspendedAt, CancellationToken ct);
}

public interface ITenantSchemaMigrator { Task MigrateAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, Guid previousSourceOrderId, string service, CancellationToken ct); }

/// <summary>Reconciles dedicated PostgreSQL resources; readiness requires credentials, CNPG and migrations.</summary>
public sealed class TenantDatabaseReconciler
{
    public const string TenantNamespace = "tenant-databases";
    public static IReadOnlyList<string> SupportedServices { get; } = Array.AsReadOnly(new[] { "basket", "catalog", "inventory", "order", "pricing" });
    private readonly ITenantCnpgBackend backend;
    private readonly ITenantSchemaMigrator migrator;
    private readonly ILogger<TenantDatabaseReconciler> logger;

    public TenantDatabaseReconciler(ITenantCnpgBackend backend, ITenantSchemaMigrator migrator, ILogger<TenantDatabaseReconciler> logger)
    {
        this.backend = backend;
        this.migrator = migrator;
        this.logger = logger;
    }

    public async Task<TenantDatabaseStatus> ReconcileAsync(TenantDatabaseDesiredState desired, CancellationToken ct, Guid sourceOrderId = default, Guid previousSourceOrderId = default)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (!Guid.TryParseExact(desired.TenantId, "D", out Guid id) || id == Guid.Empty || id.ToString("D") != desired.TenantId)
            throw new ArgumentException("TenantId must be a canonical UUID.", nameof(desired));
        string suffix = id.ToString("N");
        if ((desired.SubscriptionStatus is TenantSubscriptionStatus.Suspended or TenantSubscriptionStatus.Cancelled) && desired.SuspendedAt is DateTimeOffset suspendedAt)
        {
            await backend.SuspendAccessAsync(desired.TenantId, desired.Identifier, sourceOrderId, suspendedAt, ct).ConfigureAwait(false);
            // Retention expiry alone never authorizes destructive deletion. An independently
            // approved, signed purge request and verified backup must gate a separate purge path.
            return new(desired.TenantId, TenantDatabasePhase.Retained);
        }
        if (desired.SubscriptionStatus != TenantSubscriptionStatus.Active || !string.Equals(desired.DatabaseStrategy, "dedicated", StringComparison.OrdinalIgnoreCase)) return new(desired.TenantId, TenantDatabasePhase.NotManaged);
        if (!string.Equals(desired.DatabaseProvider, "postgres", StringComparison.OrdinalIgnoreCase)) return new(desired.TenantId, TenantDatabasePhase.NotManaged, "CNPG requires postgres provider.");
        if (desired.ReadReplicas is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(desired));

        string clusterName = "tenant-" + suffix;
        string secretName = "tenant-" + suffix + "-credentials";
        await backend.ApplyClusterAsync(new(TenantNamespace, clusterName, desired.ReadReplicas + 1, secretName,
            sourceOrderId, previousSourceOrderId), ct).ConfigureAwait(false);
        if (!await backend.HasApplicationSecretAsync(desired.TenantId, sourceOrderId, ct).ConfigureAwait(false)) return new(desired.TenantId, TenantDatabasePhase.AwaitingCredentials, "Missing current paid-period role credentials in " + TenantNamespace + ".");
        if (!await backend.IsClusterReadyAsync(TenantNamespace, clusterName, ct).ConfigureAwait(false))
            return new(desired.TenantId, TenantDatabasePhase.Provisioning, "CNPG Cluster is not ready.");
        foreach (string service in SupportedServices)
        {
            string databaseName = "tenant_" + suffix + "_" + service;
            await backend.ApplyDatabaseAsync(new(TenantNamespace, databaseName, clusterName, "tenant_" + suffix + "_" + service + "_migration"), ct).ConfigureAwait(false);
            if (!await backend.IsDatabaseReadyAsync(TenantNamespace, databaseName, ct).ConfigureAwait(false))
                return new(desired.TenantId, TenantDatabasePhase.Provisioning, "CNPG " + service + " Database is not ready.");
        }
        try
        {
            foreach (string service in SupportedServices)
                await migrator.MigrateAsync(desired, sourceOrderId, previousSourceOrderId, service, ct).ConfigureAwait(false);
            Guid? uid = await backend.GetReadyClusterUidAsync(TenantNamespace, clusterName, ct).ConfigureAwait(false);
            return uid is { } evidence && evidence != Guid.Empty
                ? new(desired.TenantId, TenantDatabasePhase.Ready, EvidenceId: evidence)
                : new(desired.TenantId, TenantDatabasePhase.Provisioning, "CNPG Cluster lost readiness before publication.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Schema migration failed for {TenantId}", desired.TenantId);
            return new(desired.TenantId, TenantDatabasePhase.Migrating, "Migration failed; retry required before readiness.");
        }
    }
}

/// <summary>Kubernetes API adapter for CNPG resources confined to tenant-databases.</summary>
public sealed class KubernetesCnpgBackend : ITenantCnpgBackend
{
    private const string Namespace = "tenant-databases";
    private const string Clusters = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/clusters";
    private const string Databases = "apis/postgresql.cnpg.io/v1/namespaces/tenant-databases/databases";
    private readonly HttpClient http;
    private readonly ITenantDatabaseRoleRevoker roleRevoker;
    private readonly ITenantOpenBaoConnectionStore openBao;
    private readonly string? snapshotClass;

    public KubernetesCnpgBackend(HttpClient http, ITenantDatabaseRoleRevoker roleRevoker,
        ITenantOpenBaoConnectionStore openBao, string? snapshotClass = null)
    {
        this.http = http;
        this.roleRevoker = roleRevoker;
        this.openBao = openBao;
        this.snapshotClass = snapshotClass;
    }

    public async Task ApplyClusterAsync(TenantClusterSpec spec, CancellationToken ct)
    {
        CheckNamespace(spec.Namespace);
        if (spec.SourceOrderId == Guid.Empty || spec.SourceOrderId == spec.PreviousSourceOrderId)
            throw new InvalidOperationException("A signed paid source order is required for CNPG provisioning.");
        await EnsureCurrentPaidOrderAsync(spec, ct).ConfigureAwait(false);
        string suffix = spec.Name[7..];
        var roles = new List<object>(11);
        await EnsureSecretAsync(spec.SecretName, "tenant_" + suffix + "_migration", ct).ConfigureAwait(false);
        string managerName = "tenant_" + suffix + "_manager";
        string managerSecret = "tenant-" + suffix + "-manager";
        await EnsureSecretAsync(managerSecret, managerName, ct).ConfigureAwait(false);
        var managerMemberships = new List<string>(12) { "pg_signal_backend", $"tenant_{suffix}_migration" };
        // Bootstrap runs before CNPG's declarative roles. PostgreSQL requires ADMIN OPTION,
        // not CREATEROLE alone, to change another role's LOGIN attribute.
        var bootstrapSql = new List<string>(24)
        {
            $"CREATE ROLE \"{managerName}\" NOLOGIN NOINHERIT CREATEROLE",
            $"GRANT \"tenant_{suffix}_migration\" TO \"{managerName}\" WITH ADMIN TRUE, INHERIT FALSE, SET FALSE",
            $"GRANT pg_signal_backend TO \"{managerName}\" WITH ADMIN FALSE, INHERIT TRUE, SET FALSE",
        };
        roles.Add(new Dictionary<string, object?>
        {
            ["name"] = managerName, ["ensure"] = "present", ["login"] = true,
            ["superuser"] = false, ["createdb"] = false, ["createrole"] = true, ["inherit"] = false,
            ["inRoles"] = managerMemberships,
            ["passwordSecret"] = new Dictionary<string, string> { ["name"] = managerSecret },
        });
        foreach (string service in TenantDatabaseReconciler.SupportedServices)
        {
            foreach (string purpose in new[] { "migration", "app" })
            {
                string roleName = $"tenant_{suffix}_{service}_{purpose}";
                managerMemberships.Add(roleName);
                bootstrapSql.Add($"CREATE ROLE \"{roleName}\" NOLOGIN");
                bootstrapSql.Add($"GRANT \"{roleName}\" TO \"{managerName}\" WITH ADMIN TRUE, INHERIT FALSE, SET FALSE");
            }
        }
        foreach (string service in TenantDatabaseReconciler.SupportedServices)
        {
            foreach (string purpose in new[] { "migration", "app" })
            {
                string name = "tenant_" + suffix + "_" + service + "_" + purpose;
                string roleSecret = $"tenant-{suffix}-{service}-{purpose}-{spec.SourceOrderId:N}";
                await EnsureSecretAsync(roleSecret, name, ct).ConfigureAwait(false);
                roles.Add(new Dictionary<string, object?>
                {
                    ["name"] = name, ["ensure"] = "present", ["login"] = true,
                    ["superuser"] = false, ["createdb"] = false, ["createrole"] = false,
                    ["passwordSecret"] = new Dictionary<string, string>
                    {
                        ["name"] = roleSecret,
                    },
                });
            }
        }
        var resource = new Dictionary<string, object?>
        {
            ["apiVersion"] = "postgresql.cnpg.io/v1", ["kind"] = "Cluster",
            ["metadata"] = new Dictionary<string, object?>
            {
                ["name"] = spec.Name, ["namespace"] = Namespace,
                ["annotations"] = new Dictionary<string, string> { ["teck.io/source-order-id"] = spec.SourceOrderId.ToString("D") },
                ["labels"] = new Dictionary<string, string?> { ["teck.io/tenant-access"] = "active", ["teck.io/suspended-at"] = null },
            },
            ["spec"] = new Dictionary<string, object?>
            {
                ["instances"] = spec.Instances,
                ["managed"] = new Dictionary<string, object?> { ["roles"] = roles },
                ["bootstrap"] = new Dictionary<string, object?> { ["initdb"] = new Dictionary<string, object?> { ["database"] = "tenant_" + suffix, ["owner"] = "tenant_" + suffix + "_migration", ["secret"] = new Dictionary<string, object?> { ["name"] = spec.SecretName }, ["postInitSQL"] = bootstrapSql } },
                ["storage"] = new Dictionary<string, object?> { ["size"] = "5Gi" }
            }
        };
        if (!string.IsNullOrWhiteSpace(snapshotClass))
        {
            ((Dictionary<string, object?>)resource["spec"]!)["backup"] = new Dictionary<string, object?>
            {
                ["volumeSnapshot"] = new Dictionary<string, string>
                {
                    ["className"] = snapshotClass,
                    ["snapshotOwnerReference"] = "none",
                },
            };
        }
        await ApplyAsync(Clusters + "/" + spec.Name, resource, ct, spec).ConfigureAwait(false);
    }

    public async Task<bool> HasApplicationSecretAsync(string tenantId, Guid sourceOrderId, CancellationToken ct)
    {
        string suffix = Guid.Parse(tenantId).ToString("N");
        if (sourceOrderId == Guid.Empty ||
            !await HasCredentialsAsync("tenant-" + suffix + "-credentials", "tenant_" + suffix + "_migration", ct).ConfigureAwait(false) ||
            !await HasCredentialsAsync("tenant-" + suffix + "-manager", "tenant_" + suffix + "_manager", ct).ConfigureAwait(false)) return false;
        foreach (string service in TenantDatabaseReconciler.SupportedServices)
        {
            if (!await HasCredentialsAsync($"tenant-{suffix}-{service}-migration-{sourceOrderId:N}", $"tenant_{suffix}_{service}_migration", ct).ConfigureAwait(false) ||
                !await HasCredentialsAsync($"tenant-{suffix}-{service}-app-{sourceOrderId:N}", $"tenant_{suffix}_{service}_app", ct).ConfigureAwait(false)) return false;
        }
        return true;
    }

    public Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct) => ReadyAsync(ns, Clusters + "/" + name, true, ct);
    public async Task<Guid?> GetReadyClusterUidAsync(string ns, string name, CancellationToken ct)
    {
        CheckNamespace(ns);
        using HttpResponseMessage response = await http.GetAsync(Clusters + "/" + name, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using JsonDocument cluster = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
            cancellationToken: ct).ConfigureAwait(false);
        JsonElement root = cluster.RootElement;
        return root.TryGetProperty("metadata", out JsonElement metadata) && metadata.TryGetProperty("uid", out JsonElement uid) &&
            Guid.TryParse(uid.GetString(), out Guid id) && id != Guid.Empty &&
            root.TryGetProperty("status", out JsonElement status) && status.TryGetProperty("phase", out JsonElement phase) &&
            phase.GetString() == "Cluster in healthy state" ? id : null;
    }

    public async Task ApplyDatabaseAsync(TenantDatabaseSpec spec, CancellationToken ct)
    {
        CheckNamespace(spec.Namespace);
        var resource = new Dictionary<string, object?>
        {
            ["apiVersion"] = "postgresql.cnpg.io/v1", ["kind"] = "Database",
            ["metadata"] = new Dictionary<string, object?> { ["name"] = spec.Name, ["namespace"] = Namespace },
            ["spec"] = new Dictionary<string, object?> { ["cluster"] = new Dictionary<string, object?> { ["name"] = spec.ClusterName }, ["name"] = spec.Name, ["owner"] = spec.Owner, ["databaseReclaimPolicy"] = "retain" }
        };
        await ApplyAsync(Databases + "/" + spec.Name, resource, ct).ConfigureAwait(false);
    }

    public Task<bool> IsDatabaseReadyAsync(string ns, string name, CancellationToken ct) => ReadyAsync(ns, Databases + "/" + name, false, ct);
    public async Task SuspendAccessAsync(string tenantId, string identifier, Guid sourceOrderId,
        DateTimeOffset suspendedAt, CancellationToken ct)
    {
        if (!Guid.TryParseExact(tenantId, "D", out Guid id) || id == Guid.Empty || sourceOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(identifier) || identifier.Contains('/') || suspendedAt == default)
            throw new InvalidOperationException("A signed tenant, paid source order and suspension time are required.");
        string suffix = id.ToString("N");
        string clusterPath = Clusters + "/tenant-" + suffix;
        using HttpResponseMessage existing = await http.GetAsync(clusterPath, ct).ConfigureAwait(false);
        if (existing.StatusCode == HttpStatusCode.NotFound)
        {
            // Without a Cluster, no PostgreSQL role can authenticate; reject orphaned live Secrets.
            string bootstrapPath = "api/v1/namespaces/" + Namespace + "/secrets/tenant-" + suffix + "-credentials";
            bool bootstrapExists = await EnsureSecretRevokedAsync(bootstrapPath, ct).ConfigureAwait(false);
            foreach (string service in TenantDatabaseReconciler.SupportedServices)
            {
                await EnsureSecretRevokedAsync("api/v1/namespaces/" + Namespace + "/secrets/tenant-" + suffix + "-" + service + "-app", ct).ConfigureAwait(false);
                await EnsureSecretRevokedAsync("api/v1/namespaces/" + Namespace + "/secrets/tenant-" + suffix + "-" + service + "-migration", ct).ConfigureAwait(false);
            }
            if (bootstrapExists)
                await PatchAsync(bootstrapPath, new
                {
                    metadata = new
                    {
                        labels = new Dictionary<string, string>
                        {
                            ["teck.io/tenant-access"] = "suspended", ["teck.io/suspended-at"] = suspendedAt.ToUniversalTime().ToString("O"),
                        },
                    },
                }, ct).ConfigureAwait(false);
        }
        else
        {
            existing.EnsureSuccessStatusCode();
            JsonNode cluster = JsonNode.Parse(await existing.Content.ReadAsStringAsync(ct).ConfigureAwait(false))
                ?? throw new InvalidOperationException("CNPG Cluster is missing its resource state.");
            if (cluster["metadata"]?["annotations"]?["teck.io/source-order-id"]?.GetValue<string>() != sourceOrderId.ToString("D"))
                throw new InvalidOperationException("A stale paid period cannot suspend another tenant purchase order.");
            if (!Guid.TryParse(cluster["metadata"]?["uid"]?.GetValue<string>(), out Guid uid) || uid == Guid.Empty ||
                cluster["spec"]?["managed"]?["roles"] is not JsonArray roles)
                throw new InvalidOperationException("CNPG tenant Cluster and managed roles are required before suspension.");
            var expected = TenantDatabaseReconciler.SupportedServices.SelectMany(service =>
                new[] { $"tenant_{suffix}_{service}_migration", $"tenant_{suffix}_{service}_app" }).ToHashSet(StringComparer.Ordinal);
            if (roles.Count != expected.Count + 1 || roles.Any(role => role is not JsonObject ||
                (role["name"]?.GetValue<string>() != $"tenant_{suffix}_manager" &&
                 !expected.Contains(role["name"]?.GetValue<string>() ?? ""))))
                throw new InvalidOperationException("CNPG tenant managed roles do not match the isolated tenant.");
            var disabled = (JsonArray)roles.DeepClone();
            foreach (JsonNode? role in disabled)
            {
                if (role is JsonObject item && expected.Contains(item["name"]?.GetValue<string>() ?? ""))
                {
                    item["login"] = false;
                    item.Remove("passwordSecret");
                    item["disablePassword"] = true;
                }
            }
            string firstSuspension = cluster["metadata"]?["labels"]?["teck.io/suspended-at"]?.GetValue<string>()
                ?? suspendedAt.ToUniversalTime().ToString("O");
            string? resourceVersion = cluster["metadata"]?["resourceVersion"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(resourceVersion))
                throw new InvalidOperationException("CNPG Cluster resourceVersion is required for a paid-period suspension.");
            var patch = new JsonArray
            {
                new JsonObject { ["op"] = "test", ["path"] = "/metadata/resourceVersion", ["value"] = resourceVersion },
                new JsonObject
                {
                    ["op"] = "test", ["path"] = "/metadata/annotations/teck.io~1source-order-id",
                    ["value"] = sourceOrderId.ToString("D"),
                },
                new JsonObject { ["op"] = "add", ["path"] = "/metadata/labels/teck.io~1tenant-access", ["value"] = "suspended" },
                new JsonObject { ["op"] = "add", ["path"] = "/metadata/labels/teck.io~1suspended-at", ["value"] = firstSuspension },
                new JsonObject { ["op"] = "add", ["path"] = "/spec/managed/roles", ["value"] = disabled },
            };
            await PatchAsync(clusterPath, patch, ct).ConfigureAwait(false);
            using HttpResponseMessage updated = await http.GetAsync(clusterPath, ct).ConfigureAwait(false);
            updated.EnsureSuccessStatusCode();
            using JsonDocument settled = await JsonDocument.ParseAsync(await updated.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            if (!RolesReconciled(settled.RootElement, uid, expected))
                throw new InvalidOperationException("CNPG has not reconciled all tenant roles to NOLOGIN.");
            await roleRevoker.RevokeAsync(id, ct).ConfigureAwait(false);
        }
        foreach (string service in TenantDatabaseReconciler.SupportedServices)
            await openBao.RevokeAsync($"tenants/{identifier}/{service}", sourceOrderId, ct).ConfigureAwait(false);
        if (existing.StatusCode != HttpStatusCode.NotFound)
        {
            foreach (string service in TenantDatabaseReconciler.SupportedServices)
            {
                foreach (string purpose in new[] { "migration", "app" })
                    await DeleteSecretAndVerifyAsync($"tenant-{suffix}-{service}-{purpose}-{sourceOrderId:N}", ct).ConfigureAwait(false);
                await DeleteSecretAndVerifyAsync(KubernetesTenantMigrationConnections.MigrationConnectionSecretName(tenantId, sourceOrderId, service), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> HasCredentialsAsync(string secretName, string username, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync("api/v1/namespaces/" + Namespace + "/secrets/" + secretName, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return await HasValidCredentialsAsync(response, username, ct).ConfigureAwait(false);
    }

    private static async Task<bool> HasValidCredentialsAsync(HttpResponseMessage response, string expectedUsername, CancellationToken ct)
    {
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        JsonElement root = json.RootElement;
        if (!root.TryGetProperty("type", out JsonElement type) || type.GetString() != "kubernetes.io/basic-auth" ||
            !root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("username", out JsonElement username) ||
            !data.TryGetProperty("password", out JsonElement password) ||
            username.GetString() != Convert.ToBase64String(Encoding.UTF8.GetBytes(expectedUsername))) return false;
        string? encoded = password.GetString();
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        Span<byte> decoded = stackalloc byte[128];
        return Convert.TryFromBase64String(encoded, decoded, out int length) && length >= 32;
    }

    private async Task EnsureCurrentPaidOrderAsync(TenantClusterSpec spec, CancellationToken ct)
    {
        using HttpResponseMessage current = await http.GetAsync(Clusters + "/" + spec.Name, ct).ConfigureAwait(false);
        if (current.StatusCode == HttpStatusCode.NotFound) return;
        current.EnsureSuccessStatusCode();
        using JsonDocument cluster = await JsonDocument.ParseAsync(await current.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        ValidateCurrentPaidOrder(cluster.RootElement, spec);
    }

    private static void ValidateCurrentPaidOrder(JsonElement root, TenantClusterSpec spec)
    {
        bool hasMetadata = root.TryGetProperty("metadata", out JsonElement metadata);
        string? owner = hasMetadata && metadata.TryGetProperty("annotations", out JsonElement annotations) &&
            annotations.TryGetProperty("teck.io/source-order-id", out JsonElement order)
                ? order.GetString() : null;
        bool suspended = hasMetadata && metadata.TryGetProperty("labels", out JsonElement labels) &&
            labels.TryGetProperty("teck.io/tenant-access", out JsonElement access) && access.GetString() == "suspended";
        if (!Guid.TryParse(owner, out Guid currentOrder) || currentOrder == Guid.Empty ||
            (currentOrder == spec.SourceOrderId && suspended) ||
            (currentOrder != spec.SourceOrderId && currentOrder != spec.PreviousSourceOrderId))
            throw new InvalidOperationException("A stale or suspended paid order cannot enable CNPG tenant roles.");
    }

    private async Task EnsureSecretAsync(string secretName, string username, CancellationToken ct)
    {
        string path = "api/v1/namespaces/" + Namespace + "/secrets/" + secretName;
        using HttpResponseMessage existing = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (existing.StatusCode != HttpStatusCode.NotFound)
        {
            existing.EnsureSuccessStatusCode();
            if (!await HasValidCredentialsAsync(existing, username, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Existing tenant role Secret is missing the required credentials or has the wrong username.");
            return;
        }

        string password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var secret = new Dictionary<string, object?>
        {
            ["apiVersion"] = "v1", ["kind"] = "Secret", ["type"] = "kubernetes.io/basic-auth",
            ["metadata"] = new Dictionary<string, string> { ["name"] = secretName, ["namespace"] = Namespace },
            ["data"] = new Dictionary<string, string>
            {
                ["username"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(username)),
                ["password"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(password)),
            },
        };
        using HttpResponseMessage created = await http.PostAsJsonAsync("api/v1/namespaces/" + Namespace + "/secrets", secret, ct).ConfigureAwait(false);
        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            using HttpResponseMessage raced = await http.GetAsync(path, ct).ConfigureAwait(false);
            raced.EnsureSuccessStatusCode();
            if (!await HasValidCredentialsAsync(raced, username, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Concurrent tenant role Secret creation yielded invalid credentials.");
            return;
        }
        created.EnsureSuccessStatusCode();
    }

    private static bool RolesReconciled(JsonElement cluster, Guid uid, HashSet<string> expected)
    {
        if (!cluster.TryGetProperty("metadata", out JsonElement metadata) || !metadata.TryGetProperty("uid", out JsonElement actualUid) ||
            !Guid.TryParse(actualUid.GetString(), out Guid actual) || actual != uid ||
            !cluster.TryGetProperty("spec", out JsonElement spec) || !spec.TryGetProperty("managed", out JsonElement managed) ||
            !managed.TryGetProperty("roles", out JsonElement roles) || roles.ValueKind != JsonValueKind.Array ||
            !cluster.TryGetProperty("status", out JsonElement status) || !status.TryGetProperty("managedRolesStatus", out JsonElement managedStatus) ||
            !managedStatus.TryGetProperty("byStatus", out JsonElement byStatus) ||
            !byStatus.TryGetProperty("reconciled", out JsonElement reconciled) || reconciled.ValueKind != JsonValueKind.Array)
            return false;
        var ready = reconciled.EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal);
        if (!expected.All(ready.Contains)) return false;
        foreach (JsonElement role in roles.EnumerateArray())
            if (role.TryGetProperty("name", out JsonElement name) && expected.Contains(name.GetString() ?? "") &&
                (!role.TryGetProperty("login", out JsonElement login) || login.ValueKind != JsonValueKind.False)) return false;
        return true;
    }

    private async Task PatchAsync(string path, object patch, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(patch) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            patch is JsonArray ? "application/json-patch+json" : "application/merge-patch+json");
        using HttpResponseMessage result = await http.SendAsync(request, ct).ConfigureAwait(false);
        result.EnsureSuccessStatusCode();
    }

    private async Task DeleteSecretAndVerifyAsync(string name, CancellationToken ct)
    {
        string path = "api/v1/namespaces/" + Namespace + "/secrets/" + name;
        using HttpResponseMessage deleted = await http.DeleteAsync(path, ct).ConfigureAwait(false);
        if (deleted.StatusCode != HttpStatusCode.NotFound) deleted.EnsureSuccessStatusCode();
        using HttpResponseMessage current = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (current.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException("Revoked tenant credential Secret remains accessible in Kubernetes.");
    }

    private async Task<bool> EnsureSecretRevokedAsync(string path, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        using JsonDocument secret = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        if (secret.RootElement.TryGetProperty("data", out JsonElement data) &&
            data.ValueKind == JsonValueKind.Object && data.EnumerateObject().Any())
        {
            throw new InvalidOperationException("Database login and OpenBao credentials must be revoked before access can be marked suspended.");
        }
        return true;
    }

    private async Task<bool> ReadyAsync(string ns, string path, bool cluster, CancellationToken ct)
    {
        CheckNamespace(ns);
        using HttpResponseMessage response = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("status", out JsonElement status)) return false;
        if (cluster)
        {
            return json.RootElement.TryGetProperty("metadata", out JsonElement metadata) &&
                metadata.TryGetProperty("uid", out JsonElement uid) &&
                Guid.TryParse(uid.GetString(), out Guid clusterUid) && clusterUid != Guid.Empty &&
                status.TryGetProperty("phase", out JsonElement phase) && phase.GetString() == "Cluster in healthy state";
        }
        return json.RootElement.TryGetProperty("metadata", out JsonElement databaseMetadata) &&
            databaseMetadata.TryGetProperty("generation", out JsonElement generation) &&
            generation.TryGetInt64(out long currentGeneration) && currentGeneration > 0 &&
            status.TryGetProperty("applied", out JsonElement applied) && applied.ValueKind == JsonValueKind.True &&
            status.TryGetProperty("observedGeneration", out JsonElement observedGeneration) &&
            observedGeneration.TryGetInt64(out long observed) && observed == currentGeneration;
    }

    private async Task ApplyAsync(string path, object resource, CancellationToken ct, TenantClusterSpec? paidOrder = null)
    {
        using HttpResponseMessage existing = await http.GetAsync(path, ct).ConfigureAwait(false);
        bool missing = existing.StatusCode == HttpStatusCode.NotFound;
        JsonArray? conditionalPatch = null;
        if (!missing)
        {
            existing.EnsureSuccessStatusCode();
            if (paidOrder is not null)
            {
                using JsonDocument cluster = await JsonDocument.ParseAsync(await existing.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
                ValidateCurrentPaidOrder(cluster.RootElement, paidOrder);
                if (!cluster.RootElement.GetProperty("metadata").TryGetProperty("resourceVersion", out JsonElement version) ||
                    string.IsNullOrWhiteSpace(version.GetString()))
                    throw new InvalidOperationException("CNPG Cluster resourceVersion is required for a paid-order update.");
                JsonNode body = JsonSerializer.SerializeToNode(resource)!;
                conditionalPatch = new JsonArray
                {
                    new JsonObject { ["op"] = "test", ["path"] = "/metadata/resourceVersion", ["value"] = version.GetString() },
                    new JsonObject
                    {
                        ["op"] = "add", ["path"] = "/metadata/annotations/teck.io~1source-order-id",
                        ["value"] = paidOrder.SourceOrderId.ToString("D"),
                    },
                    new JsonObject { ["op"] = "add", ["path"] = "/metadata/labels/teck.io~1tenant-access", ["value"] = "active" },
                    new JsonObject { ["op"] = "add", ["path"] = "/spec/instances", ["value"] = body["spec"]!["instances"]!.DeepClone() },
                    new JsonObject { ["op"] = "add", ["path"] = "/spec/managed/roles", ["value"] = body["spec"]!["managed"]!["roles"]!.DeepClone() },
                    new JsonObject { ["op"] = "add", ["path"] = "/spec/bootstrap", ["value"] = body["spec"]!["bootstrap"]!.DeepClone() },
                    new JsonObject { ["op"] = "add", ["path"] = "/spec/storage", ["value"] = body["spec"]!["storage"]!.DeepClone() },
                };
                if (cluster.RootElement.GetProperty("metadata").TryGetProperty("labels", out JsonElement labels) &&
                    labels.TryGetProperty("teck.io/suspended-at", out _))
                    conditionalPatch.Add(new JsonObject { ["op"] = "remove", ["path"] = "/metadata/labels/teck.io~1suspended-at" });
            }
        }

        using var request = new HttpRequestMessage(missing ? HttpMethod.Post : HttpMethod.Patch,
            missing ? path[..path.LastIndexOf('/')] : path)
        {
            Content = JsonContent.Create((object?)conditionalPatch ?? resource)
        };
        if (!missing)
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(conditionalPatch is null ? "application/merge-patch+json" : "application/json-patch+json");
        using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static void CheckNamespace(string ns)
    {
        if (!string.Equals(ns, Namespace, StringComparison.Ordinal)) throw new InvalidOperationException("CNPG reconciler is restricted to tenant-databases namespace.");
    }
}

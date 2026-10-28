using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Teck.TenantDatabase.Reconciler;

public enum TenantSubscriptionStatus { Pending, Active, Suspended, Cancelled }
public enum TenantDatabasePhase { Pending, NotManaged, Provisioning, AwaitingCredentials, Migrating, Ready, Retained }
public sealed record TenantDatabaseDesiredState(string TenantId, string Identifier, string DatabaseStrategy, string DatabaseProvider, int ReadReplicas, TenantSubscriptionStatus SubscriptionStatus, DateTimeOffset? SuspendedAt);
public sealed record TenantDatabaseStatus(string TenantId, TenantDatabasePhase Phase, string? Detail = null);
public sealed record TenantClusterSpec(string Namespace, string Name, int Instances, string SecretName);
public sealed record TenantDatabaseSpec(string Namespace, string Name, string ClusterName, string Owner);

/// <summary>Persisted, replay-safe request sourced from a trusted subscription snapshot.</summary>
public sealed record TenantDatabaseRequest(Guid SubscriptionId, TenantDatabaseDesiredState DesiredState, DateTimeOffset ReceivedAt, Guid SourceOrderId = default, string Proof = "", Guid PreviousSourceOrderId = default);

/// <summary>Durable queue and status store for accepted tenant database requests.</summary>
public interface ITenantDatabaseRequestStore
{
    Task<bool> UpsertAsync(TenantDatabaseRequest request, CancellationToken ct);
    Task<bool> SuspendAsync(Guid subscriptionId, Guid sourceOrderId, Guid tenantId, string identifier, DateTimeOffset suspendedAt, CancellationToken ct);
    Task<IReadOnlyList<TenantDatabaseRequest>> GetPendingAsync(int limit, CancellationToken ct);
    Task SaveStatusAsync(TenantDatabaseRequest request, TenantDatabaseStatus status, DateTimeOffset reconciledAt, CancellationToken ct);
}

/// <summary>Executes durable requests until each request reaches an externally observable state.</summary>
public sealed class TenantDatabaseWorker(ITenantDatabaseRequestStore store, TenantDatabaseReconciler reconciler, ILogger<TenantDatabaseWorker> logger)
{
    /// <summary>Reconciles the next persisted batch and stores every resulting phase.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantDatabaseRequest> requests = await store.GetPendingAsync(32, ct).ConfigureAwait(false);
        foreach (TenantDatabaseRequest request in requests)
        {
            try
            {
                TenantDatabaseStatus status = await reconciler.ReconcileAsync(request.DesiredState, ct).ConfigureAwait(false);
                await store.SaveStatusAsync(request, status, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
                logger.LogInformation("Tenant database request {SubscriptionId} for {TenantId} reached {Phase}", request.SubscriptionId, status.TenantId, status.Phase);
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
    Task<bool> HasApplicationSecretAsync(string tenantId, CancellationToken ct);
    Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct);
    Task ApplyDatabaseAsync(TenantDatabaseSpec spec, CancellationToken ct);
    Task<bool> IsDatabaseReadyAsync(string ns, string name, CancellationToken ct);
    Task SuspendAccessAsync(string tenantId, DateTimeOffset suspendedAt, CancellationToken ct);
}

public interface ITenantSchemaMigrator { Task MigrateAsync(TenantDatabaseDesiredState state, string service, CancellationToken ct); }

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

    public async Task<TenantDatabaseStatus> ReconcileAsync(TenantDatabaseDesiredState desired, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (!Guid.TryParseExact(desired.TenantId, "D", out Guid id) || id == Guid.Empty || id.ToString("D") != desired.TenantId)
            throw new ArgumentException("TenantId must be a canonical UUID.", nameof(desired));
        string suffix = id.ToString("N");
        if ((desired.SubscriptionStatus is TenantSubscriptionStatus.Suspended or TenantSubscriptionStatus.Cancelled) && desired.SuspendedAt is DateTimeOffset suspendedAt)
        {
            await backend.SuspendAccessAsync(desired.TenantId, suspendedAt, ct).ConfigureAwait(false);
            // Retention expiry alone never authorizes destructive deletion. An independently
            // approved, signed purge request and verified backup must gate a separate purge path.
            return new(desired.TenantId, TenantDatabasePhase.Retained);
        }
        if (desired.SubscriptionStatus != TenantSubscriptionStatus.Active || !string.Equals(desired.DatabaseStrategy, "dedicated", StringComparison.OrdinalIgnoreCase)) return new(desired.TenantId, TenantDatabasePhase.NotManaged);
        if (!string.Equals(desired.DatabaseProvider, "postgres", StringComparison.OrdinalIgnoreCase)) return new(desired.TenantId, TenantDatabasePhase.NotManaged, "CNPG requires postgres provider.");
        if (desired.ReadReplicas is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(desired));

        string clusterName = "tenant-" + suffix;
        string secretName = "tenant-" + suffix + "-credentials";
        await backend.ApplyClusterAsync(new(TenantNamespace, clusterName, desired.ReadReplicas + 1, secretName), ct).ConfigureAwait(false);
        if (!await backend.HasApplicationSecretAsync(desired.TenantId, ct).ConfigureAwait(false)) return new(desired.TenantId, TenantDatabasePhase.AwaitingCredentials, "Missing credentials Secret " + secretName + " in " + TenantNamespace + ".");
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
                await migrator.MigrateAsync(desired, service, ct).ConfigureAwait(false);
            return new(desired.TenantId, TenantDatabasePhase.Ready);
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

    public KubernetesCnpgBackend(HttpClient http)
    {
        this.http = http;
    }

    public async Task ApplyClusterAsync(TenantClusterSpec spec, CancellationToken ct)
    {
        CheckNamespace(spec.Namespace);
        string suffix = spec.Name[7..];
        var roles = new List<object>(10);
        await EnsureSecretAsync(spec.SecretName, "tenant_" + suffix + "_migration", ct).ConfigureAwait(false);
        foreach (string service in TenantDatabaseReconciler.SupportedServices)
        {
            foreach (string purpose in new[] { "migration", "app" })
            {
                string name = "tenant_" + suffix + "_" + service + "_" + purpose;
                await EnsureSecretAsync("tenant-" + suffix + "-" + service + "-" + purpose, name, ct).ConfigureAwait(false);
                roles.Add(new Dictionary<string, object?>
                {
                    ["name"] = name, ["ensure"] = "present", ["login"] = true,
                    ["superuser"] = false, ["createdb"] = false, ["createrole"] = false,
                    ["passwordSecret"] = new Dictionary<string, string>
                    {
                        ["name"] = "tenant-" + suffix + "-" + service + "-" + purpose,
                    },
                });
            }
        }
        var resource = new Dictionary<string, object?>
        {
            ["apiVersion"] = "postgresql.cnpg.io/v1", ["kind"] = "Cluster",
            ["metadata"] = new Dictionary<string, object?> { ["name"] = spec.Name, ["namespace"] = Namespace },
            ["spec"] = new Dictionary<string, object?>
            {
                ["instances"] = spec.Instances,
                ["managed"] = new Dictionary<string, object?> { ["roles"] = roles },
                ["bootstrap"] = new Dictionary<string, object?> { ["initdb"] = new Dictionary<string, object?> { ["database"] = "tenant_" + suffix, ["owner"] = "tenant_" + suffix + "_migration", ["secret"] = new Dictionary<string, object?> { ["name"] = spec.SecretName } } },
                ["storage"] = new Dictionary<string, object?> { ["size"] = "5Gi" }
            }
        };
        await ApplyAsync(Clusters + "/" + spec.Name, resource, ct).ConfigureAwait(false);
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

    public async Task<bool> HasApplicationSecretAsync(string tenantId, CancellationToken ct)
    {
        string suffix = Guid.Parse(tenantId).ToString("N");
        if (!await HasCredentialsAsync("tenant-" + suffix + "-credentials", "tenant_" + suffix + "_migration", ct).ConfigureAwait(false)) return false;
        foreach (string service in TenantDatabaseReconciler.SupportedServices)
        {
            if (!await HasCredentialsAsync("tenant-" + suffix + "-" + service + "-migration", "tenant_" + suffix + "_" + service + "_migration", ct).ConfigureAwait(false) ||
                !await HasCredentialsAsync("tenant-" + suffix + "-" + service + "-app", "tenant_" + suffix + "_" + service + "_app", ct).ConfigureAwait(false)) return false;
        }
        return true;
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


    public Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct) => ReadyAsync(ns, Clusters + "/" + name, true, ct);

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
    public async Task SuspendAccessAsync(string tenantId, DateTimeOffset suspendedAt, CancellationToken ct)
    {
        string suffix = Guid.Parse(tenantId).ToString("N");
        string path = "api/v1/namespaces/" + Namespace + "/secrets/tenant-" + suffix + "-credentials";
        using HttpResponseMessage response = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        response.EnsureSuccessStatusCode();
        using (JsonDocument secret = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false))
        {
            if (secret.RootElement.TryGetProperty("data", out JsonElement data) &&
                data.ValueKind == JsonValueKind.Object && data.EnumerateObject().Any())
            {
                throw new InvalidOperationException("Database login and OpenBao credentials must be revoked before access can be marked suspended.");
            }
        }

        if (suspendedAt == default) throw new ArgumentException("Suspension timestamp is required.", nameof(suspendedAt));
        var patch = new Dictionary<string, object?>
        {
            ["metadata"] = new Dictionary<string, object?> { ["labels"] = new Dictionary<string, string> { ["teck.io/tenant-access"] = "suspended", ["teck.io/suspended-at"] = suspendedAt.ToUniversalTime().ToString("O") } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(patch) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/merge-patch+json");
        using HttpResponseMessage patched = await http.SendAsync(request, ct).ConfigureAwait(false);
        patched.EnsureSuccessStatusCode();
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

    private async Task ApplyAsync(string path, object resource, CancellationToken ct)
    {
        using HttpResponseMessage existing = await http.GetAsync(path, ct).ConfigureAwait(false);
        bool missing = existing.StatusCode == HttpStatusCode.NotFound;
        if (!missing) existing.EnsureSuccessStatusCode();

        using var request = new HttpRequestMessage(missing ? HttpMethod.Post : HttpMethod.Patch,
            missing ? path[..path.LastIndexOf('/')] : path)
        {
            Content = JsonContent.Create(resource)
        };
        if (!missing)
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/merge-patch+json");
        using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static void CheckNamespace(string ns)
    {
        if (!string.Equals(ns, Namespace, StringComparison.Ordinal)) throw new InvalidOperationException("CNPG reconciler is restricted to tenant-databases namespace.");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Creates a migration connection Secret for the named service database without exposing its password to a Job manifest.</summary>
public interface ITenantMigrationConnectionProvider
{
    /// <summary>Returns the Kubernetes Secret name containing a ConnectionStrings__Default-compatible PostgreSQL connection string.</summary>
    Task<string> EnsureMigrationConnectionAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, string service, CancellationToken ct);
    /// <summary>Authenticates the app role, grants schema access, and stores its connection at the service OpenBao path.</summary>
    Task VerifyAndPublishApplicationConnectionAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, Guid previousSourceOrderId, string service, CancellationToken ct);
}

/// <summary>The service-owned migration Job has not yet completed; reconciliation must retry before readiness.</summary>
public sealed class TenantMigrationPendingException : Exception
{
    public TenantMigrationPendingException() : base("Service-owned tenant database migration has not completed.") { }
}

/// <summary>Runs each service's existing Host image in --migrate mode inside its tenant namespace.</summary>
public sealed class KubernetesTenantSchemaMigrator(HttpClient http, ITenantMigrationConnectionProvider connections,
    IReadOnlyDictionary<string, string> serviceImages, string keycloakRealm, string keycloakServerUrl) : ITenantSchemaMigrator
{
    private const string Namespace = TenantDatabaseReconciler.TenantNamespace;
    private const string Jobs = "apis/batch/v1/namespaces/tenant-databases/jobs";

    /// <inheritdoc />
    public async Task MigrateAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, Guid previousSourceOrderId, string service, CancellationToken ct)
    {
        if (!Guid.TryParseExact(state.TenantId, "D", out Guid tenantId) || tenantId == Guid.Empty ||
            sourceOrderId == Guid.Empty || !TenantDatabaseReconciler.SupportedServices.Contains(service) ||
            !serviceImages.TryGetValue(service, out string? image) || string.IsNullOrWhiteSpace(image) ||
            string.IsNullOrWhiteSpace(keycloakRealm) || string.IsNullOrWhiteSpace(keycloakServerUrl))
            throw new InvalidOperationException("A paid tenant, source order and configured service migration image are required.");

        string secretName = await connections.EnsureMigrationConnectionAsync(state, sourceOrderId, service, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secretName))
            throw new InvalidOperationException("Service migration credentials were not provisioned.");
        string order = sourceOrderId.ToString("D");
        string name = "migration-" + service + "-" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(state.TenantId + order + service)).AsSpan(0, 20));
        string path = Jobs + "/" + name;
        using HttpResponseMessage existing = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (existing.StatusCode == HttpStatusCode.NotFound)
        {
            var job = new Dictionary<string, object?>
            {
                ["apiVersion"] = "batch/v1", ["kind"] = "Job",
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["name"] = name, ["namespace"] = Namespace,
                    ["annotations"] = new Dictionary<string, string>
                    {
                        ["teck.io/source-order-id"] = order, ["teck.io/tenant-id"] = state.TenantId,
                        ["teck.io/service"] = service,
                    },
                },
                ["spec"] = new Dictionary<string, object?>
                {
                    ["backoffLimit"] = 3,
                    ["template"] = new Dictionary<string, object?>
                    {
                        ["spec"] = new Dictionary<string, object?>
                        {
                            ["restartPolicy"] = "Never",
                            ["containers"] = new[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["name"] = "migration", ["image"] = image,
                                    ["args"] = new[] { "--migrate" },
                                    ["env"] = new[]
                                    {
                                        new Dictionary<string, object?>
                                        {
                                            ["name"] = "ConnectionStrings__Default",
                                            ["valueFrom"] = new Dictionary<string, object?>
                                            {
                                                ["secretKeyRef"] = new Dictionary<string, string>
                                                {
                                                    ["name"] = secretName, ["key"] = "connection",
                                                },
                                            },
                                        },
                                        new Dictionary<string, object?> { ["name"] = "Keycloak__realm", ["value"] = keycloakRealm },
                                        new Dictionary<string, object?> { ["name"] = "Keycloak__auth-server-url", ["value"] = keycloakServerUrl },
                                        new Dictionary<string, object?> { ["name"] = "Keycloak__resource", ["value"] = service + "-api" },
                                    },
                                },
                            },
                        },
                    },
                },
            };
            using HttpResponseMessage created = await http.PostAsJsonAsync(Jobs, job, ct).ConfigureAwait(false);
            if (created.StatusCode != HttpStatusCode.Conflict)
            {
                created.EnsureSuccessStatusCode();
                throw new TenantMigrationPendingException();
            }
            using HttpResponseMessage raced = await http.GetAsync(path, ct).ConfigureAwait(false);
            raced.EnsureSuccessStatusCode();
            await VerifyJobAsync(raced, state.TenantId, order, service, image, ct).ConfigureAwait(false);
            await connections.VerifyAndPublishApplicationConnectionAsync(state, sourceOrderId, previousSourceOrderId, service, ct).ConfigureAwait(false);
            return;
        }
        existing.EnsureSuccessStatusCode();
        await VerifyJobAsync(existing, state.TenantId, order, service, image, ct).ConfigureAwait(false);
        await connections.VerifyAndPublishApplicationConnectionAsync(state, sourceOrderId, previousSourceOrderId, service, ct).ConfigureAwait(false);
    }

    private static async Task VerifyJobAsync(HttpResponseMessage response, string tenant, string order, string service,
        string image, CancellationToken ct)
    {
        using JsonDocument job = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
            cancellationToken: ct).ConfigureAwait(false);
        JsonElement root = job.RootElement;
        if (!root.TryGetProperty("metadata", out JsonElement metadata) ||
            !metadata.TryGetProperty("uid", out JsonElement uid) || !Guid.TryParse(uid.GetString(), out Guid id) || id == Guid.Empty ||
            !metadata.TryGetProperty("annotations", out JsonElement annotations) ||
            !annotations.TryGetProperty("teck.io/source-order-id", out JsonElement source) || source.GetString() != order ||
            !annotations.TryGetProperty("teck.io/tenant-id", out JsonElement owner) || owner.GetString() != tenant ||
            !annotations.TryGetProperty("teck.io/service", out JsonElement ownedService) || ownedService.GetString() != service ||
            !root.TryGetProperty("spec", out JsonElement spec) ||
            spec.GetProperty("template").GetProperty("spec").GetProperty("containers")[0].GetProperty("image").GetString() != image)
            throw new InvalidOperationException("Completed migration Job is not bound to this tenant, order and service image.");

        if (root.TryGetProperty("status", out JsonElement status) &&
            status.TryGetProperty("conditions", out JsonElement conditions) &&
            conditions.EnumerateArray().Any(condition => condition.GetProperty("type").GetString() == "Failed" &&
                condition.GetProperty("status").GetString() == "True"))
            throw new InvalidOperationException("The service-owned tenant migration Job failed.");
        if (!root.TryGetProperty("status", out status) ||
            !status.TryGetProperty("succeeded", out JsonElement succeeded) || !succeeded.TryGetInt32(out int count) || count != 1 ||
            !status.TryGetProperty("conditions", out conditions) ||
            !conditions.EnumerateArray().Any(condition => condition.GetProperty("type").GetString() == "Complete" &&
                condition.GetProperty("status").GetString() == "True"))
            throw new TenantMigrationPendingException();
    }
}

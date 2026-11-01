using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Writes a per-service application connection through OpenBao KV v2 with paid-order compare-and-swap.</summary>
public interface ITenantOpenBaoConnectionStore
{
    /// <summary>Writes the verified app connection only for the signed source order or its recorded predecessor.</summary>
    Task WriteAsync(string path, Guid sourceOrderId, Guid previousSourceOrderId, string write, CancellationToken ct);
    /// <summary>CAS-revokes the verified paid period, preserving the source order for a signed recovery.</summary>
    Task RevokeAsync(string path, Guid sourceOrderId, CancellationToken ct);
}

/// <summary>Converts operator-managed role Secrets into isolated migration and application connections.</summary>
public sealed class KubernetesTenantMigrationConnections(HttpClient kubernetes, ITenantOpenBaoConnectionStore openBao,
    Func<string, string> hostForCluster, int port, SslMode sslMode, string? rootCertificate) : ITenantMigrationConnectionProvider
{
    private const string Namespace = TenantDatabaseReconciler.TenantNamespace;
    private const string Secrets = "api/v1/namespaces/tenant-databases/secrets";
    /// <inheritdoc />
    public async Task<string> EnsureMigrationConnectionAsync(TenantDatabaseDesiredState state, Guid sourceOrderId,
        string service, CancellationToken ct)
    {
        var suffix = Validate(state, sourceOrderId, service);
        string connection = await GetConnectionAsync(suffix, service, "migration", sourceOrderId, ct).ConfigureAwait(false);
        string secretName = MigrationConnectionSecretName(state.TenantId, sourceOrderId, service);
        string path = Secrets + "/" + secretName;
        using HttpResponseMessage existing = await kubernetes.GetAsync(path, ct).ConfigureAwait(false);
        if (existing.StatusCode == HttpStatusCode.NotFound)
        {
            var resource = new Dictionary<string, object?>
            {
                ["apiVersion"] = "v1", ["kind"] = "Secret", ["type"] = "Opaque",
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["name"] = secretName, ["namespace"] = Namespace,
                    ["annotations"] = new Dictionary<string, string>
                    {
                        ["teck.io/source-order-id"] = sourceOrderId.ToString("D"),
                        ["teck.io/tenant-id"] = state.TenantId, ["teck.io/service"] = service,
                    },
                },
                ["data"] = new Dictionary<string, string> { ["connection"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(connection)) },
            };
            using HttpResponseMessage created = await kubernetes.PostAsJsonAsync(Secrets, resource, ct).ConfigureAwait(false);
            if (created.StatusCode != HttpStatusCode.Conflict)
            {
                created.EnsureSuccessStatusCode();
                return secretName;
            }
            using HttpResponseMessage raced = await kubernetes.GetAsync(path, ct).ConfigureAwait(false);
            raced.EnsureSuccessStatusCode();
            await VerifyConnectionSecretAsync(raced, connection, sourceOrderId, state.TenantId, service, ct).ConfigureAwait(false);
            return secretName;
        }
        existing.EnsureSuccessStatusCode();
        await VerifyConnectionSecretAsync(existing, connection, sourceOrderId, state.TenantId, service, ct).ConfigureAwait(false);
        return secretName;
    }

    /// <inheritdoc />
    public async Task VerifyAndPublishApplicationConnectionAsync(TenantDatabaseDesiredState state, Guid sourceOrderId,
        Guid previousSourceOrderId, string service, CancellationToken ct)
    {
        string suffix = Validate(state, sourceOrderId, service);
        string database = "tenant_" + suffix + "_" + service;
        string applicationRole = "tenant_" + suffix + "_" + service + "_app";
        string migration = await GetConnectionAsync(suffix, service, "migration", sourceOrderId, ct).ConfigureAwait(false);
        string app = await GetConnectionAsync(suffix, service, "app", sourceOrderId, ct).ConfigureAwait(false);
        await using (var owner = new NpgsqlConnection(migration))
        {
            await owner.OpenAsync(ct).ConfigureAwait(false);
            using var quote = new NpgsqlCommandBuilder();
            string quotedDatabase = quote.QuoteIdentifier(database);
            string quotedRole = quote.QuoteIdentifier(applicationRole);
            await using var grant = owner.CreateCommand();
            grant.CommandText = $"""
                REVOKE CONNECT ON DATABASE {quotedDatabase} FROM PUBLIC;
                GRANT CONNECT ON DATABASE {quotedDatabase} TO {quotedRole};
                GRANT USAGE ON SCHEMA public TO {quotedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {quotedRole};
                GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {quotedRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {quotedRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {quotedRole};
                """;
            await grant.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var verified = new NpgsqlConnection(app))
        {
            await verified.OpenAsync(ct).ConfigureAwait(false);
            await using var proof = verified.CreateCommand();
            proof.CommandText = "SELECT current_user, current_database()";
            await using var reader = await proof.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) ||
                reader.GetString(0) != applicationRole || reader.GetString(1) != database)
                throw new InvalidOperationException("Application credentials did not authenticate to the owned tenant database.");
        }
        await openBao.WriteAsync("tenants/" + state.Identifier + "/" + service, sourceOrderId, previousSourceOrderId,
            app, ct).ConfigureAwait(false);
    }

    internal static string MigrationConnectionSecretName(string tenantId, Guid sourceOrderId, string service) =>
        "tenant-migration-" + service + "-" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(tenantId + sourceOrderId.ToString("D") + service)).AsSpan(0, 16));

    private async Task<string> GetConnectionAsync(string suffix, string service, string purpose, Guid sourceOrderId, CancellationToken ct)
    {
        string role = "tenant_" + suffix + "_" + service + "_" + purpose;
        string secretName = $"tenant-{suffix}-{service}-{purpose}-{sourceOrderId:N}";
        using HttpResponseMessage response = await kubernetes.GetAsync(Secrets + "/" + secretName, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
            cancellationToken: ct).ConfigureAwait(false);
        JsonElement root = json.RootElement;
        if (!root.TryGetProperty("type", out JsonElement type) || type.GetString() != "kubernetes.io/basic-auth" ||
            !root.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("username", out JsonElement user) ||
            !data.TryGetProperty("password", out JsonElement secret))
            throw new InvalidOperationException("CNPG managed role Secret is missing basic-auth credentials.");
        string username = Encoding.UTF8.GetString(Convert.FromBase64String(user.GetString()!));
        string password = Encoding.UTF8.GetString(Convert.FromBase64String(secret.GetString()!));
        if (username != role || password.Length < 32)
            throw new InvalidOperationException("CNPG managed role credentials do not match the tenant's service role.");
        string host = hostForCluster("tenant-" + suffix);
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("CNPG cluster host is not configured.");
        if (sslMode is SslMode.VerifyCA or SslMode.VerifyFull && string.IsNullOrWhiteSpace(rootCertificate))
            throw new InvalidOperationException("Verified PostgreSQL TLS requires a mounted server CA certificate.");
        return new NpgsqlConnectionStringBuilder
        {
            Host = host, Port = port, Database = "tenant_" + suffix + "_" + service,
            Username = username, Password = password, SslMode = sslMode,
            RootCertificate = rootCertificate, Pooling = false,
        }.ConnectionString;
    }

    private static string Validate(TenantDatabaseDesiredState state, Guid order, string service)
    {
        if (state is null || !Guid.TryParseExact(state.TenantId, "D", out Guid id) || id == Guid.Empty ||
            order == Guid.Empty || !TenantDatabaseReconciler.SupportedServices.Contains(service) ||
            string.IsNullOrWhiteSpace(state.Identifier))
            throw new InvalidOperationException("Only a paid tenant's supported service can obtain database credentials.");
        return id.ToString("N");
    }

    private static async Task VerifyConnectionSecretAsync(HttpResponseMessage response, string expected,
        Guid sourceOrderId, string tenantId, string service, CancellationToken ct)
    {
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
            cancellationToken: ct).ConfigureAwait(false);
        JsonElement root = json.RootElement;
        if (!root.TryGetProperty("metadata", out JsonElement metadata) ||
            !metadata.TryGetProperty("annotations", out JsonElement labels) ||
            !labels.TryGetProperty("teck.io/source-order-id", out JsonElement order) || order.GetString() != sourceOrderId.ToString("D") ||
            !labels.TryGetProperty("teck.io/tenant-id", out JsonElement tenant) || tenant.GetString() != tenantId ||
            !labels.TryGetProperty("teck.io/service", out JsonElement serviceName) || serviceName.GetString() != service ||
            !root.TryGetProperty("data", out JsonElement data) || !data.TryGetProperty("connection", out JsonElement encoded) ||
            encoded.GetString() != Convert.ToBase64String(Encoding.UTF8.GetBytes(expected)))
            throw new InvalidOperationException("Existing migration Secret does not match the signed paid period or current role credentials.");
    }
}

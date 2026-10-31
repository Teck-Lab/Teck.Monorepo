using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Revokes all tenant PostgreSQL service and bootstrap roles before a retained status is allowed.</summary>
public interface ITenantDatabaseRoleRevoker
{
    Task RevokeAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Disables all tenant application and migration logins using a tenant-scoped role manager.</summary>
public sealed class PostgresTenantRoleRevoker(HttpClient kubernetes, Func<string, string> hostForCluster,
    int port, SslMode sslMode, string? rootCertificate) : ITenantDatabaseRoleRevoker
{
    private const string Secrets = "api/v1/namespaces/tenant-databases/secrets";

    /// <summary>Stops both new logins and pre-existing sessions; throws unless all eleven roles are blocked.</summary>
    public async Task RevokeAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A canonical tenant ID is required.", nameof(tenantId));
        string suffix = tenantId.ToString("N");
        string cluster = "tenant-" + suffix;
        string manager = "tenant_" + suffix + "_manager";
        string[] roles = ["tenant_" + suffix + "_migration", .. TenantDatabaseReconciler.SupportedServices
            .SelectMany(service => new[] { $"tenant_{suffix}_{service}_migration", $"tenant_{suffix}_{service}_app" })];
        using HttpResponseMessage response = await kubernetes.GetAsync(Secrets + "/tenant-" + suffix + "-manager", ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("Tenant role-manager Secret is missing.");
        response.EnsureSuccessStatusCode();
        using JsonDocument secret = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
            cancellationToken: ct).ConfigureAwait(false);
        JsonElement root = secret.RootElement;
        if (!root.TryGetProperty("type", out JsonElement type) || type.GetString() != "kubernetes.io/basic-auth" ||
            !root.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("username", out JsonElement encodedUser) ||
            !data.TryGetProperty("password", out JsonElement encodedPassword))
            throw new InvalidOperationException("Tenant role-manager Secret lacks basic-auth credentials.");
        string username = Encoding.UTF8.GetString(Convert.FromBase64String(encodedUser.GetString()!));
        string password = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPassword.GetString()!));
        if (username != manager || password.Length < 32) throw new InvalidOperationException("Tenant role-manager Secret has invalid credentials.");
        string host = hostForCluster(cluster);
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("Tenant PostgreSQL host is unavailable.");
        if (sslMode is SslMode.VerifyCA or SslMode.VerifyFull && string.IsNullOrWhiteSpace(rootCertificate))
            throw new InvalidOperationException("Tenant PostgreSQL CA certificate is unavailable.");
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = host, Port = port, Database = "postgres", Username = username, Password = password,
            SslMode = sslMode, RootCertificate = rootCertificate, Pooling = false,
        };
        await using var connection = new NpgsqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        foreach (string role in roles)
        {
            await using var disable = new NpgsqlCommand($"ALTER ROLE \"{role}\" NOLOGIN PASSWORD NULL", connection);
            await disable.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var terminate = new NpgsqlCommand(
            "SELECT bool_and(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE usename = ANY(@roles) AND pid <> pg_backend_pid()", connection))
        {
            terminate.Parameters.AddWithValue("roles", roles);
            object? result = await terminate.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is false) throw new InvalidOperationException("A tenant PostgreSQL session could not be terminated.");
        }
        await using var verify = new NpgsqlCommand(
            "SELECT count(*), count(*) FILTER (WHERE rolcanlogin) FROM pg_roles WHERE rolname = ANY(@roles)", connection);
        verify.Parameters.AddWithValue("roles", roles);
        await using var reader = await verify.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.GetInt64(0) != roles.Length || reader.GetInt64(1) != 0)
            throw new InvalidOperationException("All tenant roles must exist with NOLOGIN after password revocation.");
        await reader.CloseAsync().ConfigureAwait(false);
        await using var sessions = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE usename = ANY(@roles)", connection);
        sessions.Parameters.AddWithValue("roles", roles);
        if ((long)(await sessions.ExecuteScalarAsync(ct).ConfigureAwait(false))! != 0)
            throw new InvalidOperationException("Tenant PostgreSQL sessions remain active after login revocation.");
    }
}

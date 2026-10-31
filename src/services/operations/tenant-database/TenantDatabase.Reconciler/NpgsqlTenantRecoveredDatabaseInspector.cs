using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Confirms physical restore by connecting independently to every tenant database on a disposable CNPG Cluster.</summary>
public interface ITenantRecoveredDatabaseInspector
{
    /// <summary>Returns true only when all five restored application databases accept SQL queries.</summary>
    Task<bool> InspectAsync(string clusterName, Guid tenantId, CancellationToken ct);
}

/// <summary>Reads the temporary recovered Cluster's superuser Secret and checks actual PostgreSQL recovery.</summary>
/// <param name="kubernetes">Namespace-restricted Kubernetes API client.</param>
/// <param name="hostForCluster">Internal DNS for the recovered Cluster's read-write service.</param>
/// <param name="port">Recovered PostgreSQL service port.</param>
/// <param name="sslMode">TLS policy; production requires VerifyCA.</param>
/// <param name="rootCertificate">Mounted PostgreSQL CA when verification is required.</param>
public sealed class NpgsqlTenantRecoveredDatabaseInspector(HttpClient kubernetes,
    Func<string, string> hostForCluster, int port, SslMode sslMode, string? rootCertificate)
    : ITenantRecoveredDatabaseInspector
{
    private const string SecretPrefix = "api/v1/namespaces/tenant-databases/secrets/";

    /// <inheritdoc />
    public async Task<bool> InspectAsync(string clusterName, Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || clusterName != $"restore-{tenantId:N}")
            throw new InvalidOperationException("A canonical disposable tenant recovery Cluster is required.");
        if (sslMode == SslMode.VerifyCA && (string.IsNullOrWhiteSpace(rootCertificate) || !File.Exists(rootCertificate)))
            throw new InvalidOperationException("A mounted tenant PostgreSQL CA is required to verify restored data.");

        using HttpResponseMessage secret = await kubernetes.GetAsync(SecretPrefix + clusterName + "-superuser", ct).ConfigureAwait(false);
        if (secret.StatusCode == HttpStatusCode.NotFound) return false;
        secret.EnsureSuccessStatusCode();
        using JsonDocument parsed = await JsonDocument.ParseAsync(await secret.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
            cancellationToken: ct).ConfigureAwait(false);
        JsonElement root = parsed.RootElement;
        if (!root.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("username", out JsonElement user) ||
            !data.TryGetProperty("password", out JsonElement password)) return false;
        string username = Encoding.UTF8.GetString(Convert.FromBase64String(user.GetString() ?? string.Empty));
        if (username != "postgres") return false;
        string decodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(password.GetString() ?? string.Empty));
        if (string.IsNullOrWhiteSpace(decodedPassword)) return false;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = hostForCluster(clusterName),
            Port = port,
            Username = username,
            Password = decodedPassword,
            SslMode = sslMode,
            RootCertificate = rootCertificate,
            Timeout = 5,
            CommandTimeout = 5,
            Pooling = false,
        };
        string prefix = "tenant_" + tenantId.ToString("N") + "_";
        foreach (string service in TenantDatabaseReconciler.SupportedServices)
        {
            builder.Database = prefix + service;
            try
            {
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var command = new NpgsqlCommand("SELECT current_database()", connection);
                if (!string.Equals(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string,
                    builder.Database, StringComparison.Ordinal)) return false;
            }
            catch (Exception error) when (error is NpgsqlException or TimeoutException)
            {
                return false;
            }
        }

        return true;
    }
}

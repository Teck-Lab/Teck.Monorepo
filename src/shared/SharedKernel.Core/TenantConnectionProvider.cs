using Microsoft.Extensions.Configuration;

namespace SharedKernel.Core;

/// <summary>
/// Resolves tenant-specific database connection strings from configuration and environment variables.
/// </summary>
public static class TenantConnectionProvider
{
    /// <summary>
    /// Gets the tenant connection string for read or write access.
    /// </summary>
    /// <param name="config">The application configuration source.</param>
    /// <param name="tenantId">The tenant identifier used in connection string keys.</param>
    /// <param name="readOnly">Whether to resolve the read connection string; otherwise resolves write.</param>
    /// <returns>The resolved tenant connection string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no matching tenant connection string is found.</exception>
    public static string GetTenantConnection(IConfiguration config, string tenantId, bool readOnly = false)
    {
        var side = readOnly ? "Read" : "Write";
        var key = $"ConnectionStrings:Tenants:{tenantId}:{side}";
        var dsn = config[key] ?? Environment.GetEnvironmentVariable($"ConnectionStrings__Tenants__{tenantId}__{side}");
        if (string.IsNullOrWhiteSpace(dsn))
            throw new InvalidOperationException($"Tenant connection string not found for tenant '{tenantId}' (key: {key})");
        return dsn;
    }
}

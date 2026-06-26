using SharedKernel.Core.Pricing;
using SharedKernel.Infrastructure.MultiTenant;

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Interface for resolving tenant-specific database connections.
/// </summary>
public interface ITenantDbConnectionResolver
{
    /// <summary>
    /// Resolves the connection string, database provider, and strategy for a tenant.
    /// </summary>
    /// <param name="tenantInfo">The tenant information.</param>
    /// <returns>A tuple containing the connection string, database provider, and strategy.</returns>
    (string WriteConnectionString, string? ReadConnectionString, DatabaseProvider Provider, DatabaseStrategy Strategy) ResolveTenantConnection(TenantDetails tenantInfo);

    /// <summary>
    /// Safely resolves the connection string, database provider, and strategy for a tenant.
    /// This method provides better error handling and prevents fallback to shared database for dedicated tenants.
    /// </summary>
    /// <param name="tenantInfo">The tenant information.</param>
    /// <param name="requireCustomerApi">Whether to require Customer API availability for dedicated tenants.</param>
    /// <returns>A result indicating success or failure with detailed error information.</returns>
    Task<TenantConnectionResult> ResolveTenantConnectionSafelyAsync(TenantDetails tenantInfo, bool requireCustomerApi = true);
}

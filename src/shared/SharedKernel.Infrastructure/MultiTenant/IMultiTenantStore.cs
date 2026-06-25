namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Interface for a multi-tenant store.
/// </summary>
public interface IMultiTenantStore
{
    /// <summary>
    /// Gets all tenants.
    /// </summary>
    /// <returns>An array of tenant info.</returns>
    Task<TenantDetails[]> GetAllAsync();

    /// <summary>
    /// Tries to get a tenant by its identifier.
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    Task<TenantDetails?> TryGetAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// Tries to get a tenant by its ID.
    /// </summary>
    /// <param name="id">The tenant ID.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    Task<TenantDetails?> TryGetByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Tries to add a tenant.
    /// </summary>
    /// <param name="tenantInfo">The tenant info to add.</param>
    /// <returns>True if the tenant was added successfully; otherwise, false.</returns>
    Task<bool> TryAddAsync(TenantDetails tenantInfo);

    /// <summary>
    /// Tries to remove a tenant.
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <returns>True if the tenant was removed successfully; otherwise, false.</returns>
    Task<bool> TryRemoveAsync(string identifier);

    /// <summary>
    /// Tries to update a tenant.
    /// </summary>
    /// <param name="tenantInfo">The tenant info to update.</param>
    /// <returns>True if the tenant was updated successfully; otherwise, false.</returns>
    Task<bool> TryUpdateAsync(TenantDetails tenantInfo);

    /// <summary>
    /// Tries to get a tenant by its name.
    /// </summary>
    /// <param name="name">The tenant name.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    Task<TenantDetails?> TryGetByNameAsync(string name, CancellationToken cancellationToken);
}

using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Pricing;

namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Lightweight tenant store that materializes the tenant selected by the signed-claim strategy.
/// Despite its legacy name, it never reads caller-supplied HTTP headers.
/// </summary>
public sealed class HeaderTenantStore : IMultiTenantStore<TenantDetails>
{
    /// <summary>
    /// Gets all tenants.
    /// </summary>
    /// <returns>An empty tenant sequence.</returns>
    public Task<IEnumerable<TenantDetails>> GetAllAsync()
    {
        return Task.FromResult(Enumerable.Empty<TenantDetails>());
    }

    /// <inheritdoc />
    public Task<IEnumerable<TenantDetails>> GetAllAsync(int take, int skip)
    {
        _ = take;
        _ = skip;
        return Task.FromResult(Enumerable.Empty<TenantDetails>());
    }

    /// <summary>
    /// Gets a tenant by identifier.
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <returns>The resolved tenant or <see langword="null"/>.</returns>
    public Task<TenantDetails?> GetByIdentifierAsync(string identifier)
    {
        return TryGetByIdentifierAsync(identifier);
    }

    /// <summary>
    /// Gets a tenant by id.
    /// </summary>
    /// <param name="id">The tenant id.</param>
    /// <returns>The resolved tenant or <see langword="null"/>.</returns>
    public Task<TenantDetails?> GetAsync(string id)
    {
        return TryGetAsync(id);
    }

    /// <summary>
    /// Adds a tenant.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public Task<bool> AddAsync(TenantDetails tenantInfo)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Removes a tenant.
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public Task<bool> RemoveAsync(string identifier)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Updates a tenant.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public Task<bool> UpdateAsync(TenantDetails tenantInfo)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Tries to resolve a tenant by identifier.
    /// </summary>
    /// <param name="identifier">The requested tenant identifier.</param>
    /// <returns>The resolved tenant or <see langword="null"/>.</returns>
    public Task<TenantDetails?> TryGetByIdentifierAsync(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Task.FromResult<TenantDetails?>(null);
        }

        return Task.FromResult<TenantDetails?>(BuildTenant(identifier, identifier));
    }

    /// <summary>
    /// Tries to resolve a tenant by id.
    /// </summary>
    /// <param name="id">The tenant id.</param>
    /// <returns>The resolved tenant or <see langword="null"/>.</returns>
    public Task<TenantDetails?> TryGetAsync(string id)
    {
        return TryGetByIdentifierAsync(id);
    }

    /// <summary>
    /// Tries to resolve a tenant by id.
    /// </summary>
    /// <param name="id">The tenant id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved tenant or <see langword="null"/>.</returns>
    public Task<TenantDetails?> TryGetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return TryGetByIdentifierAsync(id);
    }

    /// <summary>
    /// Tries to resolve a tenant by name.
    /// </summary>
    /// <param name="name">The tenant name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved tenant or <see langword="null"/>.</returns>
    public Task<TenantDetails?> TryGetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return TryGetByIdentifierAsync(name);
    }

    /// <summary>
    /// Tries to add a tenant.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public Task<bool> TryAddAsync(TenantDetails tenantInfo)
    {
        _ = tenantInfo;
        return Task.FromResult(false);
    }

    /// <summary>
    /// Tries to remove a tenant.
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public Task<bool> TryRemoveAsync(string identifier)
    {
        _ = identifier;
        return Task.FromResult(false);
    }

    /// <summary>
    /// Tries to update a tenant.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public Task<bool> TryUpdateAsync(TenantDetails tenantInfo)
    {
        return TryAddAsync(tenantInfo);
    }

    private TenantDetails BuildTenant(string id, string identifier)
    {
        return new TenantDetails
        {
            Id = id,
            Identifier = identifier,
            Name = identifier,
            IsActive = true,
            DatabaseStrategy = DatabaseStrategy.Shared.Name,
            DatabaseProvider = string.Empty,
            Plan = string.Empty,
        };
    }
}

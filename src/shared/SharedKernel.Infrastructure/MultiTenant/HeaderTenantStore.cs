using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Pricing;

namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Lightweight tenant store that resolves tenant details directly from request context.
/// This store avoids external calls and is intended for header-driven gateway flows.
/// </summary>
public sealed class HeaderTenantStore : IMultiTenantStore<TenantDetails>
{
    private const string TenantDbStrategyHeaderName = "X-Tenant-DbStrategy";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TeckCloudMultiTenancyOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderTenantStore"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor.</param>
    /// <param name="options">Multi-tenancy options.</param>
    public HeaderTenantStore(
        IHttpContextAccessor httpContextAccessor,
        IOptions<TeckCloudMultiTenancyOptions> options)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

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

        if (!MatchesHeaderTenant(identifier))
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
        _ = _httpContextAccessor.HttpContext;
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
        _ = _httpContextAccessor.HttpContext;
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

    private bool MatchesHeaderTenant(string requestedTenantId)
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return true;
        }

        string tenantHeaderName = _options.TenantIdHeaderName;
        if (!httpContext.Request.Headers.TryGetValue(tenantHeaderName, out var headerValue))
        {
            return true;
        }

        string resolvedHeaderTenant = headerValue.ToString();
        if (string.IsNullOrWhiteSpace(resolvedHeaderTenant))
        {
            return true;
        }

        return string.Equals(resolvedHeaderTenant, requestedTenantId, StringComparison.OrdinalIgnoreCase);
    }

    private TenantDetails BuildTenant(string id, string identifier)
    {
        string strategy = ResolveDatabaseStrategyFromHeader();

        return new TenantDetails
        {
            Id = id,
            Identifier = identifier,
            Name = identifier,
            IsActive = true,
            DatabaseStrategy = strategy,
            DatabaseProvider = string.Empty,
            Plan = string.Empty,
        };
    }

    private string ResolveDatabaseStrategyFromHeader()
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return DatabaseStrategy.Shared.Name;
        }

        if (!httpContext.Request.Headers.TryGetValue(TenantDbStrategyHeaderName, out var strategyHeaderValue))
        {
            return DatabaseStrategy.Shared.Name;
        }

        string strategy = strategyHeaderValue.ToString();
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return DatabaseStrategy.Shared.Name;
        }

        if (DatabaseStrategy.TryFromName(strategy, true, out var resolvedStrategy))
        {
            return resolvedStrategy.Name;
        }

        return DatabaseStrategy.Shared.Name;
    }
}

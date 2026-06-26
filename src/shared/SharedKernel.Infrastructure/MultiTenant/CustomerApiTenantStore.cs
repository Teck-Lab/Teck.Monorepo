using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Pagination;
using SharedKernel.Core.Pricing;
using SharedKernel.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// A multi-tenant store that retrieves tenant information from the Customer API.
/// This implementation directly implements Finbuckle's IMultiTenantStore interface.
/// </summary>
public class CustomerApiTenantStore : IMultiTenantStore<TenantDetails>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFusionCache _fusionCache;
    private readonly ILogger<CustomerApiTenantStore> _logger;
    private readonly CustomerApiTenantOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomerApiTenantStore"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="fusionCache">The FusionCache instance.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The tenant options.</param>
    public CustomerApiTenantStore(
        IHttpClientFactory httpClientFactory,
        IFusionCache fusionCache,
        ILogger<CustomerApiTenantStore> logger,
        IOptions<TeckCloudMultiTenancyOptions> options)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _fusionCache = fusionCache ?? throw new ArgumentNullException(nameof(fusionCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var TeckOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options = TeckOptions.CustomerApiOptions;
    }

    /// <summary>
    /// Gets all tenants from the Customer API.
    /// </summary>
    /// <returns>An enumerable of tenant info.</returns>
    public async Task<IEnumerable<TenantDetails>> GetAllAsync()
    {
        var result = await GetPaginatedTennantsAsync(DatabaseStrategy.None, 1000, 0); // Use a reasonable limit

        return result.Items;
    }

    /// <summary>
    /// Get all tenants asynchronously with pagination.
    /// </summary>
    /// <param name="take">The number of tenants to take.</param>
    /// <param name="skip">The number of tenants to skip.</param>
    /// <returns>An enumerable of tenant info.</returns>
    public async Task<IEnumerable<TenantDetails>> GetAllAsync(int take, int skip)
    {
        // Use the existing GetAllAsync method with DatabaseStrategy.None
        try
        {
            var result = await GetPaginatedTennantsAsync(DatabaseStrategy.None, take, skip);

            return result.Items;
        }
        catch (Exception exception)
        {
            throw new NotSupportedException($"Error occurred while fetching tenants with take={take} and skip={skip}.", exception);
        }
    }

    /// <summary>
    /// Gets all tenants from the Customer API with pagination.
    /// </summary>
    /// <param name="strategy">The database strategy used to select the tenant data source.</param>
    /// <param name="size">The maximum number of tenants to return per page.</param>
    /// <param name="page">The zero-based index of the page to retrieve.</param>
    /// <returns>An enumerable of tenant info.</returns>
    public async Task<PagedList<TenantDetails>> GetPaginatedTennantsAsync(DatabaseStrategy strategy, int size, int page)
    {
        var cacheKey = FusionCacheKeys.AllTenantsPage(strategy.Name, size, page);

        var tenants = await _fusionCache.GetOrSetAsync<PagedList<TenantDetails>>(
            cacheKey,
            async _ =>
            {
                try
                {
                    var client = _httpClientFactory.CreateClient(_options.HttpClientName);
                    var endpoint = _options.AllTenantsEndpoint;

                    // Manually build the query string for take/skip
                    string separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                    var pagedEndpoint = $"{endpoint}{separator}size={Uri.EscapeDataString(size.ToString())}&page={Uri.EscapeDataString(page.ToString())}&strategy={Uri.EscapeDataString(strategy.Name)}";

                    var details = await client.GetFromJsonAsync<PagedList<TenantDetails>>(pagedEndpoint, cancellationToken: _);
                    if (details == null || details.Items.Count == 0)
                    {
                        _logger.LogWarning("No tenants found in the Customer API (size={Size}, page={Page}, strategy={Strategy})", size, page, strategy);
                        return new PagedList<TenantDetails>([], 0, 0, 0);
                    }

                    return details;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error retrieving tenants from Customer API (size={Size}, page={Page}, strategy={Strategy})", size, page, strategy);
                    return new PagedList<TenantDetails>([], 0, 0, 0);
                }
            },
            options => options.SetDuration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
        );
        return tenants;
    }

    /// <summary>
    /// Gets a tenant from the Customer API by its identifier.
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    public async Task<TenantDetails?> GetByIdentifierAsync(string identifier)
    {
        return await TryGetByIdentifierAsync(identifier);
    }

    /// <summary>
    /// Gets a tenant from the Customer API by its ID.
    /// </summary>
    /// <param name="id">The tenant ID.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    public async Task<TenantDetails?> GetAsync(string id)
    {
        return await TryGetByIdAsync(id);
    }

    /// <summary>
    /// Adds a tenant to the Customer API (not implemented).
    /// </summary>
    /// <param name="tenantInfo">The tenant info to add.</param>
    /// <returns>True if the tenant was added successfully; otherwise, false.</returns>
    public async Task<bool> AddAsync(TenantDetails tenantInfo)
    {
        return await TryAddAsync(tenantInfo);
    }

    /// <summary>
    /// Removes a tenant from the Customer API (not implemented).
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <returns>True if the tenant was removed successfully; otherwise, false.</returns>
    public async Task<bool> RemoveAsync(string identifier)
    {
        return await TryRemoveAsync(identifier);
    }

    /// <summary>
    /// Updates a tenant in the Customer API (not implemented).
    /// </summary>
    /// <param name="tenantInfo">The tenant info to update.</param>
    /// <returns>True if the tenant was updated successfully; otherwise, false.</returns>
    public async Task<bool> UpdateAsync(TenantDetails tenantInfo)
    {
        return await TryUpdateAsync(tenantInfo);
    }

    /// <summary>
    /// Gets a tenant from the Customer API by its identifier.
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    [RequiresDynamicCode("Calls HttpContent.ReadFromJsonAsync which may require dynamic code at runtime.")]
    public async Task<TenantDetails?> TryGetByIdentifierAsync(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return null;
        }

        if (Guid.TryParse(identifier, out _))
        {
            return await TryGetByIdAsync(identifier);
        }

        var cacheKey = FusionCacheKeys.TenantByIdentifier(identifier);

        var cachedTenant = await _fusionCache.TryGetAsync<TenantDetails>(cacheKey);
        if (cachedTenant.HasValue && cachedTenant.Value is not null)
        {
            return cachedTenant.Value;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(_options.HttpClientName);
            string encodedIdentifier = Uri.EscapeDataString(identifier);
            var endpoint = _options.TenantDetailsEndpoint.Replace("{tenantId}", encodedIdentifier, StringComparison.Ordinal);
            Uri requestUri = BuildRequestUri(endpoint);
            var response = await client.GetAsync(requestUri).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Tenant with identifier {Identifier} not found", identifier);
                    }
                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("Error retrieving tenant {Identifier}: {StatusCode}", identifier, response.StatusCode);
                    }
                }

                await _fusionCache.RemoveAsync(cacheKey);
                return null;
            }

            var details = await response.Content.ReadFromJsonAsync<TenantDetails>();
            if (details == null)
            {
                _logger.LogWarning("Tenant with identifier {Identifier} returned null details", identifier);
                await _fusionCache.RemoveAsync(cacheKey);
                return null;
            }

            await _fusionCache.SetAsync(
                cacheKey,
                details,
                options => options
                    .SetDuration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
                    .SetFailSafe(false));

            return details;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "HTTP error retrieving tenant {Identifier}", identifier);
            await _fusionCache.RemoveAsync(cacheKey);
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error retrieving tenant {Identifier}", identifier);
            await _fusionCache.RemoveAsync(cacheKey);
            return null;
        }
    }

    /// <summary>
    /// Alias for TryGetByIdentifierAsync, required by the interface.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    public async Task<TenantDetails?> TryGetAsync(string id)
    {
        return await TryGetByIdentifierAsync(id);
    }

    /// <summary>
    /// Gets a tenant from the Customer API by its ID.
    /// </summary>
    /// <param name="id">The tenant ID.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    [RequiresDynamicCode("Calls HttpContent.ReadFromJsonAsync which may require dynamic code at runtime.")]
    public async Task<TenantDetails?> TryGetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var cacheKey = FusionCacheKeys.TenantById(id);

        var cachedTenant = await _fusionCache.TryGetAsync<TenantDetails>(cacheKey, token: cancellationToken);
        if (cachedTenant.HasValue && cachedTenant.Value is not null)
        {
            return cachedTenant.Value;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(_options.HttpClientName);
            string encodedId = Uri.EscapeDataString(id);
            var endpoint = _options.TenantByIdEndpoint.Replace("{id}", encodedId, StringComparison.Ordinal);
            Uri requestUri = BuildRequestUri(endpoint);
            var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Tenant with ID {Id} not found", id);
                    }
                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("Error retrieving tenant with ID {Id}: {StatusCode}", id, response.StatusCode);
                    }
                }

                await _fusionCache.RemoveAsync(cacheKey, token: cancellationToken);
                return null;
            }

            var details = await response.Content.ReadFromJsonAsync<TenantDetails>(cancellationToken);
            if (details == null)
            {
                _logger.LogWarning("Tenant with ID {Id} returned null details", id);
                await _fusionCache.RemoveAsync(cacheKey, token: cancellationToken);
                return null;
            }

            await _fusionCache.SetAsync(
                cacheKey,
                details,
                options => options
                    .SetDuration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
                    .SetFailSafe(false),
                token: cancellationToken);

            return details;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "HTTP error retrieving tenant with ID {Id}", id);
            await _fusionCache.RemoveAsync(cacheKey, token: cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error retrieving tenant with ID {Id}", id);
            await _fusionCache.RemoveAsync(cacheKey, token: cancellationToken);
            return null;
        }
    }

    /// <summary>
    /// Tries to get a tenant by name.
    /// </summary>
    /// <param name="name">The tenant name.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The tenant info if found; otherwise, null.</returns>
    public async Task<TenantDetails?> TryGetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var cacheKey = FusionCacheKeys.TenantByName(name);

        var cachedTenant = await _fusionCache.TryGetAsync<TenantDetails>(cacheKey, token: cancellationToken);
        if (cachedTenant.HasValue && cachedTenant.Value is not null)
        {
            return cachedTenant.Value;
        }

        try
        {
            TenantDetails? resolvedTenant = null;

            if (!string.IsNullOrEmpty(_options.TenantByNameEndpoint))
            {
                var client = _httpClientFactory.CreateClient(_options.HttpClientName);
                var endpoint = _options.TenantByNameEndpoint.Replace("{name}", Uri.EscapeDataString(name), StringComparison.Ordinal);
                Uri requestUri = BuildRequestUri(endpoint);
                var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    resolvedTenant = await response.Content.ReadFromJsonAsync<TenantDetails>(cancellationToken);
                }
            }

            if (resolvedTenant is null)
            {
                var allTenants = await GetAllAsync();
                resolvedTenant = allTenants.FirstOrDefault(tenantInfo =>
                    string.Equals(tenantInfo.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            if (resolvedTenant is null)
            {
                await _fusionCache.RemoveAsync(cacheKey, token: cancellationToken);
                return null;
            }

            await _fusionCache.SetAsync(
                cacheKey,
                resolvedTenant,
                options => options
                    .SetDuration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
                    .SetFailSafe(false),
                token: cancellationToken);

            return resolvedTenant;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "HTTP error retrieving tenant with name {Name}", name);
            await _fusionCache.RemoveAsync(cacheKey, token: cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error retrieving tenant with name {Name}", name);
            await _fusionCache.RemoveAsync(cacheKey, token: cancellationToken);
            return null;
        }
    }

    /// <summary>
    /// Adds a tenant to the Customer API (not implemented).
    /// </summary>
    /// <param name="tenantInfo">The tenant info to add.</param>
    /// <returns>True if the tenant was added successfully; otherwise, false.</returns>
    public Task<bool> TryAddAsync(TenantDetails tenantInfo)
    {
        // This method would add a tenant via the API
        // Not implemented in this version
        _logger.LogWarning("Adding tenants via HTTP is not implemented");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Removes a tenant from the Customer API (not implemented).
    /// </summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <returns>True if the tenant was removed successfully; otherwise, false.</returns>
    public Task<bool> TryRemoveAsync(string identifier)
    {
        // This method would remove a tenant via the API
        // Not implemented in this version
        _logger.LogWarning("Removing tenants via HTTP is not implemented");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Updates a tenant in the Customer API (not implemented).
    /// </summary>
    /// <param name="tenantInfo">The tenant info to update.</param>
    /// <returns>True if the tenant was updated successfully; otherwise, false.</returns>
    public Task<bool> TryUpdateAsync(TenantDetails tenantInfo)
    {
        // This method would update a tenant via the API
        // Not implemented in this version
        _logger.LogWarning("Updating tenants via HTTP is not implemented");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Finds the primary tenant from a list of tenant IDs.
    /// </summary>
    /// <param name="tenantIds">List of tenant IDs to check.</param>
    /// <returns>The primary tenant ID if found; otherwise, the first tenant ID.</returns>
    public async Task<string?> FindPrimaryTenantIdAsync(IEnumerable<string> tenantIds)
    {
        if (tenantIds == null)
        {
            return null;
        }

        var normalizedTenantIds = tenantIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedTenantIds.Length == 0)
        {
            return null;
        }

        string cacheKey = FusionCacheKeys.PrimaryTenantIdForSet(normalizedTenantIds);
        var cachedPrimaryTenantId = await _fusionCache.TryGetAsync<string>(cacheKey);
        if (cachedPrimaryTenantId.HasValue)
        {
            return cachedPrimaryTenantId.Value;
        }

        // Get all tenants for the provided IDs
        var tenants = new List<TenantDetails>(normalizedTenantIds.Length);

        foreach (var tenantId in normalizedTenantIds)
        {
            var tenant = await TryGetByIdentifierAsync(tenantId);
            if (tenant != null)
            {
                tenants.Add(tenant);
            }
        }

        if (tenants.Count == 0)
        {
            await _fusionCache.SetAsync(
                cacheKey,
                default(string),
                options => options
                    .SetDuration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
                    .SetFailSafe(false));

            return null;
        }

        // Check if any tenant is marked as primary
        for (int tenantIndex = 0; tenantIndex < tenants.Count; tenantIndex++)
        {
            if (tenants[tenantIndex].IsPrimary)
            {
                string primaryTenantId = tenants[tenantIndex].Identifier;
                await _fusionCache.SetAsync(
                    cacheKey,
                    primaryTenantId,
                    options => options
                        .SetDuration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
                        .SetFailSafe(false));

                return primaryTenantId;
            }
        }

        // If no primary tenant is found, return the first tenant
        string fallbackTenantId = tenants[0].Identifier;
        await _fusionCache.SetAsync(
            cacheKey,
            fallbackTenantId,
            options => options
                .SetDuration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
                .SetFailSafe(false));

        return fallbackTenantId;
    }

    // All caching is now handled by FusionCache's GetOrSetAsync methods above.
    private static Uri BuildRequestUri(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? absoluteUri)
            ? absoluteUri
            : new Uri(endpoint, UriKind.Relative);
    }
}

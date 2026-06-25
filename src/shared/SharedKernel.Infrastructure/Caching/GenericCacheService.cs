using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Caching;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.MultiTenant;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Infrastructure.Caching;

/// <summary>
/// The generic cache service.
/// </summary>
/// <typeparam name="TEntity"/>
/// <typeparam name="TId"/>
/// <remarks>
/// Initializes a new instance of the <see cref="GenericCacheService{TEntity, TId}"/> class.
/// </remarks>
/// <param name="fusionCache">The fusion cache.</param>
/// <param name="genericRepository">The generic repository.</param>
/// <param name="tenantContextAccessor">The optional tenant context accessor used to scope cache keys per tenant.</param>
public class GenericCacheService<TEntity, TId>(
    IFusionCache fusionCache,
    IGenericReadRepository<TEntity, TId> genericRepository,
    IMultiTenantContextAccessor<TenantDetails>? tenantContextAccessor = null) : IGenericCacheService<TEntity, TId>
    where TEntity : class
{
    /// <summary>
    /// The fusion cache.
    /// </summary>
    private readonly IFusionCache _fusionCache = fusionCache;

    /// <summary>
    /// The repository.
    /// </summary>
    private readonly IGenericReadRepository<TEntity, TId> _repository = genericRepository;

    /// <summary>
    /// The cache key prefix.
    /// </summary>
    private readonly string _cacheKeyPrefix = typeof(TEntity).Name;

    /// <summary>
    /// The tenant context accessor.
    /// </summary>
    private readonly IMultiTenantContextAccessor<TenantDetails>? _tenantContextAccessor = tenantContextAccessor;

    /// <summary>
    /// Get or set by id asynchronously.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="enableTracking">If true, enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><![CDATA[Task<TEntity?>]]></returns>
    public async Task<TEntity?> GetOrSetByIdAsync(TId id, bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        string key = GenerateCacheKey(id!.ToString()!);

        return await _fusionCache.GetOrSetAsync<TEntity?>(
            key,
            async (context, ct) =>
            {
                TEntity? result = await _repository.FindByIdAsync(id, cancellationToken: ct);
                if (result is null)
                {
                    context.Options.Duration = TimeSpan.FromMinutes(5);
                }

                return result;
            },
            token: cancellationToken);
    }

    /// <summary>
    /// Attempts to retrieve an entity by its id from the cache asynchronously, or returns null if not found.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    public async Task<TEntity?> TryGetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        string key = GenerateCacheKey(id!.ToString()!);

        var result = await _fusionCache.TryGetAsync<TEntity?>(
            key,
            token: cancellationToken);

        if (result.HasValue)
        {
            return result.Value;
        }

        return null;
    }

    /// <summary>
    /// Get or set by id asynchronously.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="entity">The entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><![CDATA[Task]]></returns>
    public async Task SetAsync(TId id, TEntity entity, CancellationToken cancellationToken = default)
    {
        string key = GenerateCacheKey(id!.ToString()!);

        await _fusionCache.SetAsync(key, entity, token: cancellationToken);
    }

    /// <summary>
    /// Expire by id asynchronously, might not be removed, depends on the failsafe mode.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns><![CDATA[Task]]></returns>
    public async Task ExpireAsync(TId id, CancellationToken cancellationToken = default)
    {
        string key = GenerateCacheKey(id!.ToString()!);

        await _fusionCache.ExpireAsync(key, token: cancellationToken);
    }

    /// <summary>
    /// Remove by id asynchronously.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task RemoveAsync(TId id, CancellationToken cancellationToken = default)
    {
        string key = GenerateCacheKey(id!.ToString()!);

        await _fusionCache.RemoveAsync(key, token: cancellationToken);
    }

    /// <summary>
    /// Generate cache key.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>A string.</returns>
    public string GenerateCacheKey(params string[] data)
    {
        List<string> list = [_cacheKeyPrefix];

        string? tenantId = _tenantContextAccessor?.MultiTenantContext?.TenantInfo?.Id;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            list.Add($"tenant:{tenantId}");
        }

        list.AddRange(data);

        string key = string.Join(":", list);
        return key;
    }
}

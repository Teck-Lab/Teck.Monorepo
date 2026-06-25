namespace SharedKernel.Core.Caching;

/// <summary>
/// Generic interface for cache service.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
/// <typeparam name="TId"></typeparam>
public interface IGenericCacheService<TEntity, in TId>
    where TEntity : class
{
    /// <summary>
    /// Get the value from cache, and if not found then from database.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="enableTracking"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    Task<TEntity?> GetOrSetByIdAsync(TId id, bool enableTracking = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve the entity from cache by its identifier, or sets it if not present.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    Task<TEntity?> TryGetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the entity to cache.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="entity"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task SetAsync(TId id, TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Expire specific entity.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task ExpireAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove specific entity from cache.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task RemoveAsync(TId id, CancellationToken cancellationToken = default);
}

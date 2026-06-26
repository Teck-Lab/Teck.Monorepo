namespace SharedKernel.Core.Caching;

/// <summary>
/// Generic interface for cache service.
/// </summary>
/// <typeparam name="TEntity">The type of the cached entity.</typeparam>
/// <typeparam name="TId">The type of the entity's identifier.</typeparam>
public interface IGenericCacheService<TEntity, in TId>
    where TEntity : class
{
    /// <summary>
    /// Get the value from cache, and if not found then from database.
    /// </summary>
    /// <param name="id">The identifier of the entity to retrieve.</param>
    /// <param name="enableTracking">Whether change tracking should be enabled when loading from the database.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    Task<TEntity?> GetOrSetByIdAsync(TId id, bool enableTracking = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve the entity from cache by its identifier, or sets it if not present.
    /// </summary>
    /// <param name="id">The identifier of the entity to retrieve.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    Task<TEntity?> TryGetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the entity to cache.
    /// </summary>
    /// <param name="id">The identifier under which the entity is cached.</param>
    /// <param name="entity">The entity to store in the cache.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task SetAsync(TId id, TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Expire specific entity.
    /// </summary>
    /// <param name="id">The identifier of the entity to expire.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task ExpireAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove specific entity from cache.
    /// </summary>
    /// <param name="id">The identifier of the entity to remove.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task RemoveAsync(TId id, CancellationToken cancellationToken = default);
}

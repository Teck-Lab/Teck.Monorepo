using System.Linq.Expressions;

namespace SharedKernel.Core.Database;

/// <summary>
/// Generic write repository.
/// </summary>
/// <typeparam name="TEntity">The type of the entity managed by this repository.</typeparam>
/// <typeparam name="TId">The type of the entity's identifier.</typeparam>
public interface IGenericWriteRepository<TEntity, in TId> : IGenericReadRepository<TEntity, TId>
    where TEntity : class
{
    /// <summary>
    /// Add entity.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update entity.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    void Update(TEntity entity);

    /// <summary>
    /// Delete list of entities.
    /// </summary>
    /// <param name="entities">The entities to delete.</param>
    void DeleteRange(IReadOnlyList<TEntity> entities);

    /// <summary>
    /// Delete a specific entity.
    /// </summary>
    /// <param name="entity">The entity to delete.</param>
    void Delete(TEntity entity);

    /// <summary>
    /// Soft Delete list of entities without using the change tracker.
    /// </summary>
    /// <param name="ids">The identifiers of the entities to soft delete.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task ExcecutSoftDeleteAsync(IReadOnlyCollection<TId> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft delete entities matching the predicate, without using the change tracker.
    /// </summary>
    /// <param name="predicate">The predicate selecting the entities to soft delete.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task ExcecutSoftDeleteByAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete entities matching the predicate, without using the change tracker.
    /// </summary>
    /// <param name="ids">The identifiers of the entities to hard delete.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    Task ExcecutHardDeleteAsync(IReadOnlyCollection<TId> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous save operation. The task result contains the number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

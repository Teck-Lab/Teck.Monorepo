using System.Linq.Expressions;
using Ardalis.Specification;

namespace SharedKernel.Core.Database;

/// <summary>
/// Generic read repository interface using specification pattern.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TId">The entity ID type.</typeparam>
public interface IGenericReadRepository<TEntity, in TId>
    where TEntity : class
{
    /// <summary>
    /// Find by id and return found entity, or null.
    /// </summary>
    /// <param name="id">The entity ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    Task<TEntity?> FindByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds entities by a collection of IDs and returns the found entities.
    /// </summary>
    /// <param name="ids">The collection of entity IDs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A read-only list of entities found by the provided IDs.</returns>
    Task<IReadOnlyList<TEntity>> FindByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find one entity using predicate.
    /// </summary>
    /// <param name="predicate">The predicate to filter entities.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    Task<TEntity?> FindOneAsync(Expression<Func<TEntity, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all entities matching the predicate.
    /// </summary>
    /// <param name="predicate">The predicate to filter entities.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A read-only list of entities matching the predicate.</returns>
    Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all entities.
    /// </summary>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A read-only list of all entities.</returns>
    Task<IReadOnlyList<TEntity>> GetAllAsync(bool enableTracking = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if entity exist by predicate.
    /// </summary>
    /// <param name="predicate">The predicate to filter entities.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if any entity matches the predicate, otherwise false.</returns>
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single entity matching the specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, null otherwise.</returns>
    Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single entity matching the specification with tracking option.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, null otherwise.</returns>
    Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, bool enableTracking, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single projected entity matching the specification.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The projected entity if found, null otherwise.</returns>
    Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of entities matching the specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of entities matching the specification.</returns>
    Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of entities matching the specification with tracking option.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of entities matching the specification.</returns>
    Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> specification, bool enableTracking, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of projected entities matching the specification.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of projected entities matching the specification.</returns>
    Task<IReadOnlyList<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts entities matching the specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entities matching the specification.</returns>
    Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any entity matches the specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if any entity matches the specification, otherwise false.</returns>
    Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
}

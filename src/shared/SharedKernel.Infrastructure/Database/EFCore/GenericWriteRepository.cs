using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Security.Claims;
using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Core.Database;
using SharedKernel.Core.Domain;

namespace SharedKernel.Infrastructure.Database.EFCore;

/// <summary>
/// Generic repository implementation for write operations following CQRS pattern.
/// Provides protected helpers so derived write repositories can reuse specification-based
/// read queries without changing public interfaces or entity constraints.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TId">The entity ID type.</typeparam>
/// <typeparam name="TContext">The database context type.</typeparam>
public class GenericWriteRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.Interfaces)] TEntity, TId, TContext> : IGenericWriteRepository<TEntity, TId>
    where TEntity : BaseEntity
    where TContext : BaseDbContext
{
    private readonly TContext _dbContext;
    private readonly DbSet<TEntity> _dbSet;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericWriteRepository{TEntity, TId, TContext}"/> class.
    /// </summary>
    /// <param name="dbContext">The tenant-aware DbContext instance.</param>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public GenericWriteRepository(TContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _dbSet = _dbContext.Set<TEntity>();
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the entity set.
    /// </summary>
    protected DbSet<TEntity> DbSet => _dbSet;

    /// <summary>
    /// Gets the typed DbContext instance for use by derived repositories.
    /// </summary>
    protected TContext DbContext => _dbContext;

    /// <summary>
    /// Gets the HTTP context accessor used to retrieve user/tenant information.
    /// </summary>
    protected IHttpContextAccessor? HttpContextAccessor => _httpContextAccessor;

    /// <summary>
    /// Gets the specification evaluator used by derived write repositories for read queries.
    /// </summary>
    protected ISpecificationEvaluator SpecificationEvaluator { get; } = new SpecificationEvaluator();

    /// <summary>
    /// Adds the specified entity to the context.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Updates the specified entity in the context.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    public void Update(TEntity entity)
    {
        _dbSet.Update(entity);
    }

    /// <summary>
    /// Removes the specified entity from the context.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void Delete(TEntity entity)
    {
        _dbSet.Remove(entity);
    }

    /// <summary>
    /// Removes the provided entities from the context.
    /// </summary>
    /// <param name="entities">The entities to remove.</param>
    public void DeleteRange(IReadOnlyList<TEntity> entities)
    {
        _dbSet.RemoveRange(entities);
    }

    /// <summary>
    /// Performs a soft delete for entities with the specified ids.
    /// </summary>
    /// <param name="ids">The ids to soft delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task ExcecutSoftDeleteAsync(IReadOnlyCollection<TId> ids, CancellationToken cancellationToken = default)
    {
        string? currentUserId = HttpContextAccessor?.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var idList = ids as IList<TId> ?? ids.ToList();

        await _dbSet.Where(entity => idList.Contains((TId)(object)entity.Id)).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(entity => entity.IsDeleted, true)
                .SetProperty(entity => entity.DeletedOn, DateTimeOffset.UtcNow)
                .SetProperty(entity => entity.DeletedBy, currentUserId),
            cancellationToken);
    }

    /// <summary>
    /// Performs a soft delete for entities matching the provided predicate.
    /// </summary>
    /// <param name="predicate">The predicate used to select entities to soft delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task ExcecutSoftDeleteByAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        string? currentUserId = HttpContextAccessor?.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        await _dbSet.Where(predicate).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(entity => entity.IsDeleted, true)
                .SetProperty(entity => entity.DeletedOn, DateTimeOffset.UtcNow)
                .SetProperty(entity => entity.DeletedBy, currentUserId),
            cancellationToken);
    }

    /// <summary>
    /// Performs a hard delete for entities with the specified ids.
    /// </summary>
    /// <param name="ids">The ids to hard delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task ExcecutHardDeleteAsync(IReadOnlyCollection<TId> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids as IList<TId> ?? ids.ToList();
        await _dbSet.Where(entity => idList.Contains((TId)(object)entity.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Persists changes to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Finds an entity by its identifier.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entity if found; otherwise <c>null</c>.</returns>
    public async Task<TEntity?> FindByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        var result = await _dbSet.FirstOrDefaultAsync(entity => entity.Id!.Equals(id!), cancellationToken);
        return result;
    }

    /// <summary>
    /// Finds entities by a collection of identifiers.
    /// </summary>
    /// <param name="ids">The collection of identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of found entities.</returns>
    public async Task<IReadOnlyList<TEntity>> FindByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids as IList<TId> ?? ids.ToList();
        var query = _dbSet.Where(entity => idList.Contains((TId)(object)entity.Id));
        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Finds a single entity matching the given predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <param name="enableTracking">Whether to enable EF change tracking for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching entity if found; otherwise <c>null</c>.</returns>
    public async Task<TEntity?> FindOneAsync(Expression<Func<TEntity, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();
        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Finds all entities matching the given predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <param name="enableTracking">Whether to enable EF change tracking for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of matching entities.</returns>
    public async Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();
        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.Where(predicate).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns all entities from the set.
    /// </summary>
    /// <param name="enableTracking">Whether to enable EF change tracking for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of all entities.</returns>
    public async Task<IReadOnlyList<TEntity>> GetAllAsync(bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();
        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Determines whether any entities match the specified predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <param name="enableTracking">Whether to enable EF change tracking for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if any entity matches; otherwise <c>false</c>.</returns>
    public async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();
        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.AnyAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Returns the first entity matching the given specification, or <c>null</c>.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first matching entity, or <c>null</c>.</returns>
    public async Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the first entity matching the given specification, with an option to enable tracking.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="enableTracking">Whether to enable tracking for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first matching entity, or <c>null</c>.</returns>
    public async Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, bool enableTracking, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, enableTracking).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the first projected result matching the given specification, or <c>null</c>.
    /// </summary>
    /// <typeparam name="TResult">The projection result type.</typeparam>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected result, or <c>null</c>.</returns>
    public async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Returns a list of entities matching the given specification.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of matching entities.</returns>
    public async Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns a list of entities matching the given specification with an option to enable tracking.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="enableTracking">Whether to enable tracking for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of matching entities.</returns>
    public async Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> specification, bool enableTracking, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, enableTracking).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns a list of projected results matching the given specification.
    /// </summary>
    /// <typeparam name="TResult">The projection result type.</typeparam>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of projected results.</returns>
    public async Task<IReadOnlyList<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Counts entities matching the given specification.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of matching entities.</returns>
    public async Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).CountAsync(cancellationToken);
    }

    /// <summary>
    /// Determines whether any entities match the given specification.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if any entity matches; otherwise <c>false</c>.</returns>
    public async Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Applies a specification to the current DbSet.
    /// Derived write repositories can call this to avoid duplicating specification application logic.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="enableTracking">If true, enables change tracking on the returned query.</param>
    /// <returns>An <see cref="IQueryable{TEntity}"/> that represents the query with the specification applied.</returns>
    protected IQueryable<TEntity> ApplySpecification(ISpecification<TEntity> specification, bool enableTracking = false)
    {
        var query = _dbSet.AsQueryable();
        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return SpecificationEvaluator.GetQuery(query, specification);
    }

    /// <summary>
    /// Applies a projected specification to the current DbSet.
    /// </summary>
    /// <typeparam name="TResult">The projection result type.</typeparam>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="enableTracking">If true, enables change tracking on the returned query.</param>
    /// <returns>An <see cref="IQueryable{TResult}"/> that represents the projected query with the specification applied.</returns>
    protected IQueryable<TResult> ApplySpecification<TResult>(ISpecification<TEntity, TResult> specification, bool enableTracking = false)
    {
        var query = _dbSet.AsQueryable();
        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return SpecificationEvaluator.GetQuery(query, specification);
    }
}

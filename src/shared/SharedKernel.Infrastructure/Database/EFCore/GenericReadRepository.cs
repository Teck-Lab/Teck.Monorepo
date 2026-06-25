using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Core.Database;
using SharedKernel.Core.Domain;

namespace SharedKernel.Infrastructure.Database.EFCore;

/// <summary>
/// Generic repository implementation for read operations following CQRS pattern.
/// </summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The model ID type.</typeparam>
/// <typeparam name="TContext">The database context type.</typeparam>
public class GenericReadRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.Interfaces)] TReadModel, TId, TContext> : IGenericReadRepository<TReadModel, TId>
    where TReadModel : class, IReadModel<TId>
    where TContext : BaseDbContext
{
    /// <summary>
    /// The strongly-typed tenant-aware DbContext.
    /// </summary>
    private readonly TContext _dbContext;

    /// <summary>
    /// Gets the strongly-typed tenant-aware DbContext.
    /// </summary>
    protected TContext DbContext => _dbContext;

    /// <summary>
    /// The entity set backing field.
    /// </summary>
    private readonly DbSet<TReadModel> _dbSet;

    /// <summary>
    /// Gets the entity set.
    /// </summary>
    protected DbSet<TReadModel> DbSet => _dbSet;

    /// <summary>
    /// The specification evaluator backing field.
    /// </summary>
    private readonly ISpecificationEvaluator _specificationEvaluator;

    /// <summary>
    /// Gets the specification evaluator.
    /// </summary>
    protected ISpecificationEvaluator SpecificationEvaluator => _specificationEvaluator;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericReadRepository{TReadModel, TId, TContext}"/> class.
    /// </summary>
    /// <param name="dbContext">The tenant-aware DbContext.</param>
    public GenericReadRepository(TContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = _dbContext.Set<TReadModel>();
        _specificationEvaluator = new SpecificationEvaluator();
    }

    /// <summary>
    /// Checks if entities matching the predicate exist.
    /// </summary>
    /// <param name="predicate">The predicate to filter entities.</param>
    /// <param name="enableTracking">Whether to enable tracking (default: false for read repositories).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if any entities match the predicate, false otherwise.</returns>
    public Task<bool> ExistsAsync(Expression<Func<TReadModel, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        // Create a specification from the predicate
        var spec = new GenericSpecification<TReadModel>(predicate);
        return ApplySpecification(spec, enableTracking).AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Finds entities matching the predicate.
    /// </summary>
    /// <param name="predicate">The predicate to filter entities.</param>
    /// <param name="enableTracking">Whether to enable tracking (default: false for read repositories).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A read-only list of entities matching the predicate.</returns>
    public async Task<IReadOnlyList<TReadModel>> FindAsync(Expression<Func<TReadModel, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        // Create a specification from the predicate
        var spec = new GenericSpecification<TReadModel>(predicate);
        var result = await ApplySpecification(spec, enableTracking).ToListAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Finds an entity by ID.
    /// </summary>
    /// <param name="id">The entity ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    public async Task<TReadModel?> FindByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        // Create a specification for finding by ID
        var spec = new ByIdSpecification<TReadModel, TId>(id);
        return await ApplySpecification(spec).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Finds a single entity matching any of the provided IDs.
    /// </summary>
    /// <param name="ids">The collection of entity IDs to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    public async Task<IReadOnlyList<TReadModel>> FindByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default)
    {
        // Create a specification for finding by ID
        var spec = new ByIdsSpecification<TReadModel, TId>(ids);
        return await ApplySpecification(spec).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Finds a single entity matching the predicate.
    /// </summary>
    /// <param name="predicate">The predicate to filter entities.</param>
    /// <param name="enableTracking">Whether to enable tracking (default: false for read repositories).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    public async Task<TReadModel?> FindOneAsync(Expression<Func<TReadModel, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        // Create a specification from the predicate
        var spec = new GenericSpecification<TReadModel>(predicate);
        return await ApplySpecification(spec, enableTracking).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets all entities.
    /// </summary>
    /// <param name="enableTracking">Whether to enable tracking (default: false for read repositories).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A read-only list of all entities.</returns>
    public async Task<IReadOnlyList<TReadModel>> GetAllAsync(bool enableTracking = false, CancellationToken cancellationToken = default)
    {
        // Create an empty specification (no filters)
        var spec = new Specification<TReadModel>();
        var result = await ApplySpecification(spec, enableTracking).ToListAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Applies the specification to the database set.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <returns>The filtered queryable.</returns>
    protected virtual IQueryable<TReadModel> ApplySpecification(ISpecification<TReadModel> specification, bool enableTracking = false)
    {
        var query = DbSet.AsQueryable();
        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return SpecificationEvaluator.GetQuery(query, specification);
    }

    /// <summary>
    /// Applies the specification to the database set for a single result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result after projection.</typeparam>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <returns>The filtered queryable.</returns>
    protected virtual IQueryable<TResult> ApplySpecification<TResult>(ISpecification<TReadModel, TResult> specification, bool enableTracking = false)
    {
        var query = DbSet.AsQueryable();
        if (!enableTracking)
        {
            query = query.AsNoTracking();
        }

        return SpecificationEvaluator.GetQuery(query, specification);
    }

    /// <summary>
    /// Gets a single entity matching the specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, null otherwise.</returns>
    public async Task<TReadModel?> FirstOrDefaultAsync(ISpecification<TReadModel> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, false).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a single entity matching the specification with tracking option.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, null otherwise.</returns>
    public async Task<TReadModel?> FirstOrDefaultAsync(ISpecification<TReadModel> specification, bool enableTracking, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, enableTracking).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a single projected entity matching the specification.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The projected entity if found, null otherwise.</returns>
    public async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TReadModel, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a list of entities matching the specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of entities matching the specification.</returns>
    public async Task<IReadOnlyList<TReadModel>> ListAsync(ISpecification<TReadModel> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, false).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a list of entities matching the specification with tracking option.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="enableTracking">Whether to enable tracking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of entities matching the specification.</returns>
    public async Task<IReadOnlyList<TReadModel>> ListAsync(ISpecification<TReadModel> specification, bool enableTracking, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, enableTracking).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a list of projected entities matching the specification.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of projected entities matching the specification.</returns>
    public async Task<IReadOnlyList<TResult>> ListAsync<TResult>(ISpecification<TReadModel, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Counts entities matching the specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entities matching the specification.</returns>
    public async Task<int> CountAsync(ISpecification<TReadModel> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, false).CountAsync(cancellationToken);
    }

    /// <summary>
    /// Checks if any entity matches the specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if any entity matches the specification, otherwise false.</returns>
    public async Task<bool> AnyAsync(ISpecification<TReadModel> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, false).AnyAsync(cancellationToken);
    }
}

/// <summary>
/// Specification for finding an entity by ID.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TId">The ID type.</typeparam>
public class ByIdSpecification<TEntity, TId> : Specification<TEntity>
    where TEntity : class, IReadModel<TId>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ByIdSpecification{TEntity, TId}"/> class.
    /// </summary>
    /// <param name="id">The entity ID to match.</param>
    public ByIdSpecification(TId id)
    {
        // Using object.Equals to handle both reference and value types
        Query.Where(entity => object.Equals(entity.Id, id));
    }
}

/// <summary>
/// Specification for finding entities by one or more IDs.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TId">The ID type.</typeparam>
public class ByIdsSpecification<TEntity, TId> : Specification<TEntity>
    where TEntity : class, IReadModel<TId>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ByIdsSpecification{TEntity, TId}"/> class
    /// for a single id (keeps backward compatibility).
    /// </summary>
    /// <param name="id">The entity ID to match.</param>
    public ByIdsSpecification(TId id)
    {
        Query.Where(entity => object.Equals(entity.Id, id));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ByIdsSpecification{TEntity, TId}"/> class
    /// for multiple ids.
    /// </summary>
    /// <param name="ids">The collection of IDs to match.</param>
    public ByIdsSpecification(IEnumerable<TId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        // Make a concrete set to avoid multiple enumeration and for faster lookups.
        var idSet = new HashSet<TId>(ids);

        // If the set is empty, add a clause that always evaluates false to return no results.
        if (idSet.Count == 0)
        {
            Query.Where(_ => false);
            return;
        }

        // Use Contains on the HashSet. EF Core translates this to SQL IN(...) for supported
        // primitive ID types. This also avoids repeated enumeration of the input collection.
        Query.Where(entity => idSet.Contains(entity.Id));
    }
}

/// <summary>
/// Generic specification using a predicate.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class GenericSpecification<TEntity> : Specification<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenericSpecification{TEntity}"/> class.
    /// </summary>
    /// <param name="predicate">The predicate to filter entities.</param>
    public GenericSpecification(Expression<Func<TEntity, bool>> predicate)
    {
        Query.Where(predicate);
    }
}

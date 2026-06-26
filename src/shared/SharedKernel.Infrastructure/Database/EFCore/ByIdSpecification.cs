using Ardalis.Specification;
using SharedKernel.Core.Domain;

namespace SharedKernel.Infrastructure.Database.EFCore;

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

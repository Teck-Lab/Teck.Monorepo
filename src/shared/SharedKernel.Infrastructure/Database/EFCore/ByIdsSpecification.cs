using Ardalis.Specification;
using SharedKernel.Core.Domain;

namespace SharedKernel.Infrastructure.Database.EFCore;

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

using System.Linq.Expressions;
using Ardalis.Specification;

namespace SharedKernel.Infrastructure.Database.EFCore;

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

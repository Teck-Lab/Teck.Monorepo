using Baskets.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Baskets.Host.Database;

/// <summary>
/// Basket write repository bound to <see cref="BasketDbContext"/> so the three-type-parameter
/// <see cref="GenericWriteRepository{TEntity, TId, TContext}"/> can be registered as an open generic.
/// </summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The basket write context.</param>
/// <param name="httpContextAccessor">The HTTP context accessor used for audit stamping on bulk deletes.</param>
public sealed class BasketWriteRepository<TEntity, TId>(BasketDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : GenericWriteRepository<TEntity, TId, BasketDbContext>(dbContext, httpContextAccessor)
    where TEntity : BaseEntity;

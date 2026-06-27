using Orders.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Orders.Host.Database;

/// <summary>
/// Order write repository bound to <see cref="OrderDbContext"/> so the three-type-parameter
/// <see cref="GenericWriteRepository{TEntity, TId, TContext}"/> can be registered as an open generic.
/// </summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The order write context.</param>
/// <param name="httpContextAccessor">The HTTP context accessor used for audit stamping on bulk deletes.</param>
public sealed class OrderWriteRepository<TEntity, TId>(OrderDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : GenericWriteRepository<TEntity, TId, OrderDbContext>(dbContext, httpContextAccessor)
    where TEntity : BaseEntity;

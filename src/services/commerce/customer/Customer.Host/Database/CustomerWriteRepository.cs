using Customers.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Customers.Host.Database;

/// <summary>
/// Customer write repository bound to <see cref="CustomerDbContext"/> so the three-type-parameter
/// <see cref="GenericWriteRepository{TEntity, TId, TContext}"/> can be registered as an open generic.
/// </summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The customer write context.</param>
/// <param name="httpContextAccessor">The HTTP context accessor used for audit stamping on bulk deletes.</param>
public sealed class CustomerWriteRepository<TEntity, TId>(CustomerDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : GenericWriteRepository<TEntity, TId, CustomerDbContext>(dbContext, httpContextAccessor)
    where TEntity : BaseEntity;

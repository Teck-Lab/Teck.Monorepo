using Catalog.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Catalog.Host.Database;

/// <summary>
/// Catalog write repository bound to <see cref="CatalogDbContext"/> so the three-type-parameter
/// <see cref="GenericWriteRepository{TEntity, TId, TContext}"/> can be registered as an open generic.
/// </summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The catalog write context.</param>
/// <param name="httpContextAccessor">The HTTP context accessor used for audit stamping on bulk deletes.</param>
public sealed class CatalogWriteRepository<TEntity, TId>(CatalogDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : GenericWriteRepository<TEntity, TId, CatalogDbContext>(dbContext, httpContextAccessor)
    where TEntity : BaseEntity;

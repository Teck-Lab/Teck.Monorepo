using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Catalog.Host.Database;

/// <summary>
/// Catalog read repository bound to <see cref="CatalogReadDbContext"/> (NoTracking) so the
/// three-type-parameter <see cref="GenericReadRepository{TReadModel, TId, TContext}"/> can be
/// registered as an open generic.
/// </summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The catalog read context.</param>
public sealed class CatalogReadRepository<TReadModel, TId>(CatalogReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, CatalogReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;

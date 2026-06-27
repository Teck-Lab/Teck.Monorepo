using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Orders.Host.Database;

/// <summary>
/// Order read repository bound to <see cref="OrderReadDbContext"/> (NoTracking) so the
/// three-type-parameter <see cref="GenericReadRepository{TReadModel, TId, TContext}"/> can be
/// registered as an open generic.
/// </summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The order read context.</param>
public sealed class OrderReadRepository<TReadModel, TId>(OrderReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, OrderReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;

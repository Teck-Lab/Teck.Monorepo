using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Baskets.Host.Database;

/// <summary>
/// Basket read repository bound to <see cref="BasketReadDbContext"/> (NoTracking) so the
/// three-type-parameter <see cref="GenericReadRepository{TReadModel, TId, TContext}"/> can be
/// registered as an open generic.
/// </summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The basket read context.</param>
public sealed class BasketReadRepository<TReadModel, TId>(BasketReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, BasketReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;

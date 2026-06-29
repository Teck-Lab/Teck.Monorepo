using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Customers.Host.Database;

/// <summary>
/// Customer read repository bound to <see cref="CustomerReadDbContext"/> (NoTracking) so the
/// three-type-parameter <see cref="GenericReadRepository{TReadModel, TId, TContext}"/> can be
/// registered as an open generic.
/// </summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The customer read context.</param>
public sealed class CustomerReadRepository<TReadModel, TId>(CustomerReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, CustomerReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;

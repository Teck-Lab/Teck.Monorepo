using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Billings.Host.Database;

/// <summary>
/// Billing read repository bound to <see cref="BillingReadDbContext"/> (NoTracking) so the
/// three-type-parameter <see cref="GenericReadRepository{TReadModel, TId, TContext}"/> can be
/// registered as an open generic.
/// </summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The billing read context.</param>
public sealed class BillingReadRepository<TReadModel, TId>(BillingReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, BillingReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;

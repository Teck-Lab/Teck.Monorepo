using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Pricing.Host.Database;

/// <summary>Pricing read repository bound to <see cref="PricingReadDbContext"/> (NoTracking).</summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The pricing read context.</param>
public sealed class PricingReadRepository<TReadModel, TId>(PricingReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, PricingReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;

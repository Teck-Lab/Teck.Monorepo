using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Inventories.Host.Database;

/// <summary>
/// Inventory read repository bound to <see cref="InventoryReadDbContext"/> (NoTracking) so the
/// three-type-parameter <see cref="GenericReadRepository{TReadModel, TId, TContext}"/> can be
/// registered as an open generic.
/// </summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The inventory read context.</param>
public sealed class InventoryReadRepository<TReadModel, TId>(InventoryReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, InventoryReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;

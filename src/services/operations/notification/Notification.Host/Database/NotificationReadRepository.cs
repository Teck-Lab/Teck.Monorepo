using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Notifications.Host.Database;

/// <summary>Open generic notification read repository bound to the no-tracking context.</summary>
/// <typeparam name="TEntity">The read-model entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The no-tracking notification database context.</param>
public sealed class NotificationReadRepository<TEntity, TId>(NotificationReadDbContext dbContext) : GenericReadRepository<TEntity, TId, NotificationReadDbContext>(dbContext) where TEntity : class, IReadModel<TId> { }

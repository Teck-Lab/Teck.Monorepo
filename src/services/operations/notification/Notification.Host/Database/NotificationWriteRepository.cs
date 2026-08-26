using Notifications.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Notifications.Host.Database;

/// <summary>Open generic notification write repository bound to the tracked context.</summary>
/// <typeparam name="TEntity">The entity type persisted by the repository.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The tracked notification database context.</param>
/// <param name="httpContextAccessor">The current HTTP context accessor.</param>
public sealed class NotificationWriteRepository<TEntity, TId>(NotificationDbContext dbContext, IHttpContextAccessor httpContextAccessor) : GenericWriteRepository<TEntity, TId, NotificationDbContext>(dbContext, httpContextAccessor) where TEntity : BaseEntity { }

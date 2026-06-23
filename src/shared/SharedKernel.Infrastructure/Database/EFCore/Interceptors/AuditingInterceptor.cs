using System.Runtime.InteropServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Core.Domain;

namespace SharedKernel.Infrastructure.Database.EFCore.Interceptors
{
    /// <summary>
    /// The auditing interceptor.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="AuditingInterceptor"/> class.
    /// </remarks>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    public sealed class AuditingInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
    {
        /// <summary>
        /// The http context accessor.
        /// </summary>
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

        /// <summary>
        /// Saving the changes asynchronously.
        /// </summary>
        /// <param name="eventData">The event data.</param>
        /// <param name="result">The result.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><![CDATA[ValueTask<InterceptionResult<int>>]]></returns>
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                string? currentUserId = _httpContextAccessor?.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                UpdateAuditableEntities(eventData.Context, currentUserId);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        /// <summary>
        /// Update auditable entities.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="currentUserId">The current user id.</param>
        private static void UpdateAuditableEntities(DbContext context, string? currentUserId)
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            foreach (ref EntityEntry<IAuditable> entry in CollectionsMarshal.AsSpan(context.ChangeTracker.Entries<IAuditable>().ToList()))
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.SetCreatedByProperties(currentUserId);
                        break;
                    case EntityState.Modified:
                        entry.Entity.SetUpdatedProperties(utcNow, currentUserId);
                        break;
                    case EntityState.Detached:
                        break;
                    case EntityState.Unchanged:
                        break;
                    case EntityState.Deleted:
                        break;
                    default:
                        break;
                }
            }
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Core.Domain;

namespace SharedKernel.Infrastructure.Database.EFCore.Interceptors
{
    /// <summary>
    /// The soft delete interceptor.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="SoftDeleteInterceptor"/> class.
    /// </remarks>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    public sealed class SoftDeleteInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
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
            string? currentUserId = _httpContextAccessor?.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (eventData.Context is null)
            {
                return base.SavingChangesAsync(
                    eventData, result, cancellationToken);
            }

            IEnumerable<EntityEntry<ISoftDeletable>> entries =
                eventData
                    .Context
                    .ChangeTracker
                    .Entries<ISoftDeletable>()
                    .Where(entity => entity.State == EntityState.Deleted);

            foreach (EntityEntry<ISoftDeletable> softDeletable in entries)
            {
                softDeletable.State = EntityState.Modified;
                softDeletable.Entity.SetDeletedProperties(true, currentUserId);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

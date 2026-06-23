using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;

namespace SharedKernel.Infrastructure.Database.MultiTenant
{
    /// <summary>
    /// Represents a tenant-specific DbContext accessor.
    /// </summary>
    /// <typeparam name="TContext">The type of the DbContext.</typeparam>
    public interface ICurrentTenantDbContext<out TContext>
    where TContext : BaseDbContext
    {
        /// <summary>
        /// Gets the tenant-specific DbContext instance.
        /// </summary>
        TContext DbContext { get; }
    }

    /// <summary>
    /// Provides access to the current tenant-specific <see cref="DbContext"/> instance.
    /// </summary>
    /// <typeparam name="TContext">The type of the DbContext.</typeparam>
    public class CurrentTenantDbContext<TContext> : ICurrentTenantDbContext<TContext>
    where TContext : BaseDbContext
    {
        private readonly IDbContextFactory<TContext> _factory;
        private TContext? _cached;

        /// <summary>
        /// Initializes a new instance of the <see cref="CurrentTenantDbContext{TContext}"/> class.
        /// </summary>
        /// <param name="factory">The factory used to create <typeparamref name="TContext"/> instances.</param>
        public CurrentTenantDbContext(IDbContextFactory<TContext> factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Gets the tenant-specific <typeparamref name="TContext"/> instance.
        /// </summary>
        public TContext DbContext => _cached ??= _factory.CreateDbContext();
    }
}

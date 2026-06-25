using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Orders.Application.Database;
using SharedKernel.Infrastructure.MultiTenant;

namespace Orders.Host.Database;

/// <summary>
/// Read-only database context for orders that disables change tracking.
/// </summary>
/// <param name="options">The options used to configure the database context.</param>
/// <param name="tenantContextAccessor">The accessor providing the current tenant context.</param>
public class OrderReadDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : OrderDbContext(options, tenantContextAccessor)
{
    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}

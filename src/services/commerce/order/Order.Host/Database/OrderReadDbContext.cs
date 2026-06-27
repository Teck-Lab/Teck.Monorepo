using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Orders.Application.Database;
using SharedKernel.Infrastructure.MultiTenant;

namespace Orders.Host.Database;

/// <summary>
/// The order read context (change tracking disabled).
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor providing the current tenant context.</param>
public class OrderReadDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : OrderDbContextBase(options, tenantContextAccessor)
{
    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}

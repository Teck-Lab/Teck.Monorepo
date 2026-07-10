using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Pricing.Application.Database;
using SharedKernel.Infrastructure.MultiTenant;

namespace Pricing.Host.Database;

/// <summary>The pricing read context (change tracking disabled).</summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor providing the current tenant context.</param>
public class PricingReadDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : PricingDbContextBase(options, tenantContextAccessor)
{
    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}

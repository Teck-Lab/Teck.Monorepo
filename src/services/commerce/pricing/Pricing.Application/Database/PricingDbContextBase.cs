using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Pricing.Domain.Entities;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Pricing.Application.Database;

/// <summary>
/// Abstract pricing context defining the entity model exactly once. The write and read contexts
/// derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public abstract class PricingDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked price lists.</summary>
    public DbSet<PriceList> PriceLists => Set<PriceList>();

    /// <summary>Gets the set of tracked prices.</summary>
    public DbSet<Price> Prices => Set<Price>();

    /// <summary>Gets the set of tracked exchange rates.</summary>
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned-type configuration must run before base.OnModelCreating so Finbuckle does not
        // discover owned collections (Prices.Tiers, PriceList.Scope) as plain entities.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PricingDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

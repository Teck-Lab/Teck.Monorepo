using Baskets.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Baskets.Application.Database;

/// <summary>
/// Abstract basket context that defines the entity model exactly once. The write and read
/// contexts derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public abstract class BasketDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked baskets.</summary>
    public DbSet<Basket> Baskets => Set<Basket>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned-type configuration must run before base.OnModelCreating so Finbuckle does not
        // discover Basket.Items (OwnsMany) as a plain entity before it is marked owned.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BasketDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

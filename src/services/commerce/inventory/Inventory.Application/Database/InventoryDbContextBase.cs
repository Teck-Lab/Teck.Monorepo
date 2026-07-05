using Finbuckle.MultiTenant.Abstractions;
using Inventories.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Inventories.Application.Database;

/// <summary>
/// Abstract inventory context that defines the entity model exactly once. The write and read
/// contexts derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public abstract class InventoryDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked stock items.</summary>
    public DbSet<StockItem> StockItems => Set<StockItem>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Orders.Domain.Entities;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Orders.Application.Database;

/// <summary>
/// Abstract order context that defines the entity model exactly once. The write context
/// (<see cref="OrderDbContext"/>) and the read context (<c>OrderReadDbContext</c>) derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant for multi-tenant isolation.</param>
public abstract class OrderDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked orders.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned-type configurations must run before base.OnModelCreating so that
        // Finbuckle's ConfigureMultiTenant() does not discover Order.Lines (OwnsMany)
        // as a plain entity type before it is marked owned.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

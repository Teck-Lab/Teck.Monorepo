using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Orders.Domain.Entities;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Orders.Application.Database;

/// <summary>
/// Write-side EF Core context for the order service that enforces tenant isolation and acts as the unit of work.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor that provides the current tenant context.</param>
public class OrderDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>
    /// Gets the set of tracked orders.
    /// </summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned-type configurations must run before base.OnModelCreating so that
        // Finbuckle's ConfigureMultiTenant() does not discover Order.Lines (OwnsMany)
        // as a plain entity type before it is marked owned.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

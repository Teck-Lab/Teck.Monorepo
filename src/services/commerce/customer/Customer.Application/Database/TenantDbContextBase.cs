using Customers.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Customers.Application.Database;

/// <summary>Abstract customer context defining the entity model once for the read/write leaves.</summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant for multi-tenant isolation.</param>
public abstract class TenantDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tenants (global registry; not tenant-filtered).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Gets the set of customers.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenantDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

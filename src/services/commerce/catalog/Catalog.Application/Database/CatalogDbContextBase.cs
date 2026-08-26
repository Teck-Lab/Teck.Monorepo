using Catalog.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.Application.Database;

/// <summary>
/// Abstract catalog context that defines the entity model exactly once. The write context
/// (<see cref="CatalogDbContext"/>) and the read context (<c>CatalogReadDbContext</c>) derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant for multi-tenant isolation.</param>
public abstract class CatalogDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the products.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Gets the categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Gets the suppliers.</summary>
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned-type configurations must run before base.OnModelCreating so that
        // Finbuckle's ConfigureMultiTenant() does not discover Variant/VariantSupplier/
        // SupplierPriceHistory as plain entity types before they are marked owned.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Product>().IsMultiTenant();
        modelBuilder.Entity<Category>().IsMultiTenant();
        modelBuilder.Entity<Supplier>().IsMultiTenant();
    }
}

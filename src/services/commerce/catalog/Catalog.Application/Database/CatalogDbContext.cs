using Catalog.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.Application.Database;

/// <summary>
/// The catalog write context (tracked). The DbContext is the unit of work.
/// Plan 3 adds <c>CatalogReadDbContext : CatalogDbContext</c> (NoTracking) + Npgsql + migrations in the Host.
/// </summary>
public class CatalogDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

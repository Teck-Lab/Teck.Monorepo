using Billings.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Billings.Application.Database;

/// <summary>
/// Abstract billing context that defines the entity model exactly once. The write and read
/// contexts derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public abstract class BillingDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked payments.</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Gets the set of provider payment attempts.</summary>
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    /// <summary>Gets the set of tracked invoices.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

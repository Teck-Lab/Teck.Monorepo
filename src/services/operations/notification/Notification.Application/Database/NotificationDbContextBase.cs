using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Entities;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Notifications.Application.Database;

/// <summary>Defines the notification model once for read and write contexts.</summary>
/// <param name="options">The database context options.</param>
/// <param name="tenantContextAccessor">The current tenant context accessor.</param>
public abstract class NotificationDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor) : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets notification deliveries.</summary>
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    /// <summary>Gets durable deterministic email sender receipts.</summary>
    public DbSet<StubEmailAcceptance> StubEmailAcceptances => Set<StubEmailAcceptance>();
    /// <summary>Gets customer contact projections.</summary>
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<NotificationDelivery>().IsMultiTenant();
        modelBuilder.Entity<StubEmailAcceptance>().IsMultiTenant();
        modelBuilder.Entity<CustomerContact>().IsMultiTenant();
    }
}

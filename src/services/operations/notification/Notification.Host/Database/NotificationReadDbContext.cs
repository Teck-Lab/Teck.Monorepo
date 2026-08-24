using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Database;
using SharedKernel.Infrastructure.MultiTenant;

namespace Notifications.Host.Database;

/// <summary>Notification read context with EF tracking disabled.</summary>
/// <param name="options">The database context options.</param>
/// <param name="tenantContextAccessor">The current tenant context accessor.</param>
public sealed class NotificationReadDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor) : NotificationDbContextBase(options, tenantContextAccessor)
{
    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}

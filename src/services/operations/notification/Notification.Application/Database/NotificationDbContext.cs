using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Notifications.Application.Database;

/// <summary>Tracked notification write context and migration target.</summary>
/// <param name="options">The database context options.</param>
/// <param name="tenantContextAccessor">The current tenant context accessor.</param>
public sealed class NotificationDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor) : NotificationDbContextBase(options, tenantContextAccessor);

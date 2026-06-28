using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Orders.Application.Database;

/// <summary>
/// The order write context (change tracking enabled). Owns EF Core migrations.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant for multi-tenant isolation.</param>
public class OrderDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : OrderDbContextBase(options, tenantContextAccessor);

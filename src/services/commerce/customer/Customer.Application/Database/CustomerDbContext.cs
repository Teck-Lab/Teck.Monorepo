using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Customers.Application.Database;

/// <summary>The customer write context (tracking enabled). Owns EF Core migrations.</summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant for multi-tenant isolation.</param>
public class CustomerDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : TenantDbContextBase(options, tenantContextAccessor);

using Ardalis.Specification;
using Customers.Domain.Entities;

namespace Customers.Application.Tenants.ReadModels;

/// <summary>Selects the tenant matching the supplied identifier.</summary>
public sealed class TenantByIdSpec : Specification<Tenant>
{
    /// <summary>Initializes a new instance of the <see cref="TenantByIdSpec"/> class.</summary>
    /// <param name="tenantId">The tenant identifier to match.</param>
    public TenantByIdSpec(Guid tenantId) => Query.Where(tenant => tenant.Id == tenantId);
}

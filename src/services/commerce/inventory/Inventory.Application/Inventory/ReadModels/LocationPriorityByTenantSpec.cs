using Ardalis.Specification;
using Inventories.Domain.Entities;

namespace Inventories.Application.Inventory.ReadModels;

/// <summary>Selects the tenant's location priority list, if one exists.</summary>
public sealed class LocationPriorityByTenantSpec : Specification<LocationPriority>
{
    /// <summary>Initializes a new instance of the <see cref="LocationPriorityByTenantSpec"/> class.</summary>
    /// <param name="tenantId">The tenant identifier to match.</param>
    public LocationPriorityByTenantSpec(string tenantId) => Query.Where(priority => priority.TenantId == tenantId);
}

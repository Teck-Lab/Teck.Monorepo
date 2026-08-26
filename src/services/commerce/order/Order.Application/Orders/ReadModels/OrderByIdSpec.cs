using Ardalis.Specification;
using Orders.Domain.Entities;

namespace Orders.Application.Orders.ReadModels;

/// <summary>
/// Specification that selects a single order matching the supplied identifier.
/// </summary>
public sealed class OrderByIdSpec : Specification<Order>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderByIdSpec"/> class.
    /// </summary>
    /// <param name="orderId">The identifier of the order to match.</param>
    /// <param name="tenantId">The tenant authorized to read the order.</param>
    public OrderByIdSpec(Guid orderId, string tenantId)
    {
        Query.Where(order => order.Id == orderId && order.TenantId == tenantId);
    }
}

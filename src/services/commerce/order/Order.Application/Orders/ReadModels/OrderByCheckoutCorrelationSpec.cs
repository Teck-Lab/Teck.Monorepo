using Ardalis.Specification;
using Orders.Domain.Entities;

namespace Orders.Application.Orders.ReadModels;

/// <summary>Selects an order by its tenant-scoped checkout correlation.</summary>
public sealed class OrderByCheckoutCorrelationSpec : Specification<Order>
{
    /// <summary>Initializes the specification for the supplied checkout correlation.</summary>
    /// <param name="checkoutCorrelationId">The stable checkout idempotency key.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    public OrderByCheckoutCorrelationSpec(string checkoutCorrelationId, string tenantId) => Query.Where(order => order.TenantId == tenantId && order.CheckoutCorrelationId == checkoutCorrelationId);
}

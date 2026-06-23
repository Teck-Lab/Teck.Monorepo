using Ardalis.Specification;
using Order.Domain.Entities;

namespace Order.Application.Orders.ReadModels;

public sealed class OrderByIdSpec : Specification<Order>
{
    public OrderByIdSpec(Guid orderId)
    {
        Query.Where(order => order.Id == orderId);
    }
}

using Ardalis.Specification;
using Orders.Domain.Entities;

namespace Orders.Application.Orders.ReadModels;

public sealed class OrderByIdSpec : Specification<Order>
{
    public OrderByIdSpec(Guid orderId)
    {
        Query.Where(order => order.Id == orderId);
    }
}

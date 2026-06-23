using Ardalis.Specification;
using Order.Application.Orders.ReadModels;
using Order.Domain.Entities;

namespace Order.Application.Orders.Repositories;

public sealed class OrdersByCustomerSpec : Specification<Order, OrderSummaryDto>
{
    public OrdersByCustomerSpec(Guid customerId)
    {
        Query
            .Where(order => order.CustomerId == customerId)
            .Include(order => order.Lines)
            .OrderByDescending(order => order.CreatedAt)
            .Take(50)
            .Select(order => new OrderSummaryDto(
                order.Id,
                order.CustomerId,
                order.Status.Name,
                order.Total,
                order.CreatedAt));
    }
}

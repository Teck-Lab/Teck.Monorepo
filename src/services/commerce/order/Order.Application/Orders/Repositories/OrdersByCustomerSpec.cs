using Ardalis.Specification;
using Orders.Application.Orders.ReadModels;
using Orders.Domain.Entities;

namespace Orders.Application.Orders.Repositories;

/// <summary>
/// Specification that projects a customer's most recent orders into summary form.
/// </summary>
public sealed class OrdersByCustomerSpec : Specification<Order, OrderSummaryDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersByCustomerSpec"/> class.
    /// </summary>
    /// <param name="customerId">The identifier of the customer whose orders are retrieved.</param>
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

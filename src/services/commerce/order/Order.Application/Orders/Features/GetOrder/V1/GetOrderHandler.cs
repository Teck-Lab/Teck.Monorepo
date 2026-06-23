using Ardalis.Specification;
using ErrorOr;
using Order.Application.Orders.Mapping;
using Order.Application.Orders.ReadModels;
using Order.Application.Orders.Responses;
using Order.Domain.Entities;

namespace Order.Application.Orders.Features.GetOrder.V1;

public static class GetOrderHandler
{
    public static async Task<ErrorOr<OrderDto>> Handle(
        GetOrderQuery query,
        IRepositoryBase<Order> repository,
        CancellationToken ct)
    {
        var spec = new OrderByIdSpec(query.OrderId);
        var order = await repository.FirstOrDefaultAsync(spec, ct).ConfigureAwait(false);

        return order is null
            ? Error.NotFound(description: $"Order '{query.OrderId}' was not found.")
            : OrderMapper.ToDto(order);
    }
}

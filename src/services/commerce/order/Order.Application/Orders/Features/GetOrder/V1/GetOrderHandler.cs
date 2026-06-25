using Ardalis.Specification;
using ErrorOr;
using Orders.Application.Orders.Mapping;
using Orders.Application.Orders.ReadModels;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;

namespace Orders.Application.Orders.Features.GetOrder.V1;

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

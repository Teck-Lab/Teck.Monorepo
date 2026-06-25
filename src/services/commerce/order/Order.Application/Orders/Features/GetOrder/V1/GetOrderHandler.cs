using Ardalis.Specification;
using ErrorOr;
using Orders.Application.Orders.Mapping;
using Orders.Application.Orders.ReadModels;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;

namespace Orders.Application.Orders.Features.GetOrder.V1;

/// <summary>
/// Handles the <see cref="GetOrderQuery"/> by loading an order and mapping it to a response.
/// </summary>
public static class GetOrderHandler
{
    /// <summary>
    /// Retrieves the requested order or a not-found error when it does not exist.
    /// </summary>
    /// <param name="query">The query identifying the order to load.</param>
    /// <param name="repository">The repository used to query orders.</param>
    /// <param name="ct">A token used to cancel the operation.</param>
    /// <returns>The matching <see cref="OrderDto"/> or a not-found error.</returns>
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

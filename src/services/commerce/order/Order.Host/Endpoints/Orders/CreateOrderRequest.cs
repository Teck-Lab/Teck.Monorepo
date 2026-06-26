using Orders.Application.Orders.Features.CreateOrder.V1;

namespace Orders.Host.Endpoints.Orders;

/// <summary>
/// Request payload for creating a new order.
/// </summary>
/// <param name="CustomerId">The identifier of the customer placing the order.</param>
/// <param name="Lines">The lines to include in the order.</param>
public sealed record CreateOrderRequest(Guid CustomerId, List<CreateOrderLine> Lines);

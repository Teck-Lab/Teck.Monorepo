using Orders.Application.Orders.Responses;
using SharedKernel.Core.CQRS;

namespace Orders.Application.Orders.Features.CreateOrder.V1;

/// <summary>
/// Command that creates a new order for a customer from the supplied line items.
/// </summary>
public sealed record CreateOrderCommand(
    Guid CustomerId,
    List<CreateOrderLine> Lines) : ICommand<OrderDto>;

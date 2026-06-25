using Orders.Application.Orders.Responses;
using SharedKernel.Core.CQRS;

namespace Orders.Application.Orders.Features.CreateOrder.V1;

public sealed record CreateOrderLine(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

public sealed record CreateOrderCommand(
    Guid CustomerId,
    List<CreateOrderLine> Lines) : ICommand<OrderDto>;

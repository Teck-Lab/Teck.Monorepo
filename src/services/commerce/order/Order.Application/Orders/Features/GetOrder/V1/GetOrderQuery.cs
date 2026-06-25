using Orders.Application.Orders.Responses;
using SharedKernel.Core.CQRS;

namespace Orders.Application.Orders.Features.GetOrder.V1;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

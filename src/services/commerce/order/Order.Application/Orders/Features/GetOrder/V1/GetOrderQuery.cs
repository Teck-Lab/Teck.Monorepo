using SharedKernel.Core.CQRS;
using Order.Application.Orders.Responses;

namespace Order.Application.Orders.Features.GetOrder.V1;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

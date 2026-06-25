using SharedKernel.Core.CQRS;
using Orders.Application.Orders.Responses;

namespace Orders.Application.Orders.Features.GetOrder.V1;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

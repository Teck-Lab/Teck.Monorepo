using Orders.Application.Orders.Responses;
using SharedKernel.Core.CQRS;

namespace Orders.Application.Orders.Features.GetOrder.V1;

/// <summary>
/// Query that retrieves a single order by its identifier.
/// </summary>
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

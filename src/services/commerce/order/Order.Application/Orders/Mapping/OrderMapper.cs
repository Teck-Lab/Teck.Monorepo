using Riok.Mapperly.Abstractions;
using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.ReadModels;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;

namespace Orders.Application.Orders.Mapping;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class OrderMapper
{
    [MapProperty("Status.Name", nameof(OrderDto.Status))]
    public static partial OrderDto ToDto(this Order entity);

    [MapProperty("Status.Name", nameof(OrderSummaryDto.Status))]
    public static partial OrderSummaryDto ToSummaryDto(this Order entity);

    public static partial IReadOnlyList<OrderSummaryDto> ToDtoList(this IEnumerable<Order> entities);

    public static (Guid CustomerId, string TenantId, List<OrderLine> Lines) ToEntity(this CreateOrderCommand command)
    {
        var lines = command.Lines
            .Select(line => new OrderLine(
                line.ProductId,
                line.ProductName,
                line.Quantity,
                line.UnitPrice))
            .ToList();

        return (command.CustomerId, string.Empty, lines);
    }
}

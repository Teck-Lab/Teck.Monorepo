using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.ReadModels;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Orders.Application.Orders.Mapping;

/// <summary>
/// Mapperly-generated mappings between order entities and their data transfer objects.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class OrderMapper
{
    /// <summary>
    /// Maps an <see cref="Order"/> entity to an <see cref="OrderDto"/>.
    /// </summary>
    /// <param name="entity">The order entity to map.</param>
    /// <returns>The mapped order response.</returns>
    [MapProperty("Status.Name", nameof(OrderDto.Status))]
    public static partial OrderDto ToDto(this Order entity);

    /// <summary>
    /// Maps an <see cref="Order"/> entity to an <see cref="OrderSummaryDto"/>.
    /// </summary>
    /// <param name="entity">The order entity to map.</param>
    /// <returns>The mapped order summary.</returns>
    [MapProperty("Status.Name", nameof(OrderSummaryDto.Status))]
    public static partial OrderSummaryDto ToSummaryDto(this Order entity);

    /// <summary>
    /// Maps a sequence of <see cref="Order"/> entities to a read-only list of summaries.
    /// </summary>
    /// <param name="entities">The order entities to map.</param>
    /// <returns>The mapped order summaries.</returns>
    public static partial IReadOnlyList<OrderSummaryDto> ToDtoList(this IEnumerable<Order> entities);

    /// <summary>
    /// Maps a <see cref="CreateOrderCommand"/> to the primitive values required to construct an order.
    /// </summary>
    /// <param name="command">The command to map.</param>
    /// <returns>The customer identifier, tenant identifier, and order lines.</returns>
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

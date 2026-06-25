using Orders.Application.Database;
using Orders.Application.Orders.IntegrationEvents;
using Orders.Application.Orders.Mapping;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using Wolverine;

namespace Orders.Application.Orders.Features.CreateOrder.V1;

public static class CreateOrderHandler
{
    public static async Task<OrderDto> Handle(
        CreateOrderCommand command,
        OrderDbContext db,
        IMessageBus bus,
        CancellationToken ct)
    {
        var (customerId, tenantId, lines) = OrderMapper.ToEntity(command);
        var order = Order.Create(customerId, tenantId, lines);

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new OrderPlacedIntegrationEvent(order)).ConfigureAwait(false);

        return OrderMapper.ToDto(order);
    }
}

using Order.Application.Orders.Mapping;
using Order.Application.Orders.IntegrationEvents;
using Order.Application.Orders.Responses;
using Order.Domain.Entities;
using Order.Host.Database;
using Wolverine;

namespace Order.Application.Orders.Features.CreateOrder.V1;

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

        await bus.PublishAsync(new OrderPlacedIntegrationEvent(order), ct).ConfigureAwait(false);

        return OrderMapper.ToDto(order);
    }
}

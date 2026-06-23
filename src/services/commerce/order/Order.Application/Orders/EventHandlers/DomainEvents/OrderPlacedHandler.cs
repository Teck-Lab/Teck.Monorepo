using Order.Application.Orders.IntegrationEvents;
using Order.Domain.DomainEvents;
using Wolverine;

namespace Order.Application.Orders.EventHandlers.DomainEvents;

public static class OrderPlacedHandler
{
    public static async Task Handle(OrderPlaced domainEvent, IMessageBus bus)
    {
        await bus.PublishAsync(new OrderPlacedIntegrationEvent(domainEvent)).ConfigureAwait(false);
    }
}

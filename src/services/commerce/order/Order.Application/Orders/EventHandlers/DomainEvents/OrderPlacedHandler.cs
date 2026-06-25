using Orders.Application.Orders.IntegrationEvents;
using Orders.Domain.DomainEvents;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.DomainEvents;

public static class OrderPlacedHandler
{
    public static async Task Handle(OrderPlaced domainEvent, IMessageBus bus)
    {
        await bus.PublishAsync(new OrderPlacedIntegrationEvent(domainEvent)).ConfigureAwait(false);
    }
}

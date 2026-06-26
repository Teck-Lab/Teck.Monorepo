using Orders.Application.Orders.IntegrationEvents;
using Orders.Domain.DomainEvents;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.DomainEvents;

/// <summary>
/// Handles the <see cref="OrderPlaced"/> domain event by publishing the corresponding integration event.
/// </summary>
public static class OrderPlacedHandler
{
    /// <summary>
    /// Publishes an <see cref="OrderPlacedIntegrationEvent"/> in response to an order being placed.
    /// </summary>
    /// <param name="domainEvent">The domain event describing the placed order.</param>
    /// <param name="bus">The message bus used to publish the integration event.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    public static async Task Handle(OrderPlaced domainEvent, IMessageBus bus)
    {
        await bus.PublishAsync(new OrderPlacedIntegrationEvent(domainEvent)).ConfigureAwait(false);
    }
}

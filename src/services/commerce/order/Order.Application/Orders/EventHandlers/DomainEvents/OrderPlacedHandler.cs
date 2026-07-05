using Orders.Domain.DomainEvents;
using SharedKernel.Events;
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
        await bus.PublishAsync(new OrderPlacedIntegrationEvent
        {
            OrderId = domainEvent.OrderId,
            CustomerId = domainEvent.CustomerId,
            TenantId = domainEvent.TenantId,
            Status = domainEvent.Status,
            Total = domainEvent.Total,
            CreatedAt = domainEvent.CreatedAt,
            Lines = domainEvent.Lines
                .Select(line => new OrderPlacedLine(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, line.Total))
                .ToList(),
        }).ConfigureAwait(false);
    }
}

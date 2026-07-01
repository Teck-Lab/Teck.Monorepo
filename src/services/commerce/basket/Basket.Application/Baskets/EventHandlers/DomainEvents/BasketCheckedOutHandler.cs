using Baskets.Domain.DomainEvents;
using SharedKernel.Events;
using Wolverine;

namespace Baskets.Application.Baskets.EventHandlers.DomainEvents;

/// <summary>Publishes the integration event when a basket is checked out.</summary>
public static class BasketCheckedOutHandler
{
    /// <summary>Publishes a <see cref="BasketCheckedOutIntegrationEvent"/> for the domain event.</summary>
    /// <param name="domainEvent">The domain event.</param>
    /// <param name="bus">The message bus.</param>
    /// <returns>A task representing the publish operation.</returns>
    public static async Task Handle(BasketCheckedOut domainEvent, IMessageBus bus)
    {
        var integrationEvent = new BasketCheckedOutIntegrationEvent
        {
            BasketId = domainEvent.BasketId,
            CustomerId = domainEvent.CustomerId,
            TenantId = domainEvent.TenantId,
            Subtotal = domainEvent.Subtotal,
            CheckedOutAt = domainEvent.CheckedOutAt,
            Items = domainEvent.Items
                .Select(item => new BasketCheckedOutLine(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity, item.LineTotal))
                .ToList(),
        };

        await bus.PublishAsync(integrationEvent).ConfigureAwait(false);
    }
}

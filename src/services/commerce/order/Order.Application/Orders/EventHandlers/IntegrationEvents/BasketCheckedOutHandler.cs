using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Creates an order in response to a basket being checked out.</summary>
public static class BasketCheckedOutHandler
{
    /// <summary>Maps the checkout event to a create-order command and dispatches it.</summary>
    /// <param name="integrationEvent">The basket checkout event.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task Handle(BasketCheckedOutIntegrationEvent integrationEvent, IMessageBus bus, CancellationToken ct)
    {
        if (integrationEvent.CustomerId is not Guid customerId)
        {
            // Guest checkout without a customer cannot yet create an order; ignored until guest checkout exists.
            return;
        }

        var lines = integrationEvent.Items
            .Select(item => new CreateOrderLine(item.ProductId, item.ProductName, item.Quantity, item.UnitPrice))
            .ToList();

        await bus.InvokeAsync<OrderDto>(new CreateOrderCommand(customerId, lines), ct).ConfigureAwait(false);
    }
}

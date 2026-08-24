using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Creates orders from platform-priced version-two basket checkout events.</summary>
public static class BasketCheckedOutV2Handler
{
    /// <summary>Maps the authoritative checkout event into the internal create command.</summary>
    /// <param name="integrationEvent">The platform-priced checkout event.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after the create command is handled.</returns>
    public static async Task Handle(BasketCheckedOutV2IntegrationEvent integrationEvent, IMessageBus bus, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var command = new CreateOrderCommand(
            integrationEvent.CustomerId,
            integrationEvent.KeycloakSubjectId,
            integrationEvent.BasketId,
            integrationEvent.TenantId,
            integrationEvent.AuthorizedAmount,
            integrationEvent.Currency,
            integrationEvent.PaymentMethodToken,
            integrationEvent.SourceCorrelationId,
            integrationEvent.Items.Select(item => new CreateOrderLine(item.ProductId, item.ProductName, item.Quantity, item.UnitPrice)).ToList());
        await bus.InvokeAsync<OrderDto>(command, ct).ConfigureAwait(false);
    }
}

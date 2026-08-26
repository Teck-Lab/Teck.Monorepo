using Inventories.Application.Inventory.Features.ReleaseReservation.V1;
using SharedKernel.Events;
using Wolverine;

namespace Inventories.Application.Inventory.EventHandlers.IntegrationEvents;

/// <summary>Consumes lifecycle stock-release requests.</summary>
public static class StockReleaseRequestedHandler
{
    /// <summary>Translates the integration event into the inventory release command.</summary>
    /// <param name="evt">The release request event.</param>
    /// <param name="bus">The message bus used to invoke the command.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the invocation.</returns>
    public static Task Handle(
        StockReleaseRequestedIntegrationEvent evt,
        IMessageBus bus,
        CancellationToken ct) =>
        bus.InvokeAsync(new ReleaseReservationCommand(
            evt.OrderId,
            evt.BasketId,
            evt.TenantId,
            evt.SourceCorrelationId,
            evt.RequestId), ct);
}

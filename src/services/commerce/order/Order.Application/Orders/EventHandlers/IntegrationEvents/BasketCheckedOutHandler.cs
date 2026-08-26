using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Consumes the frozen V1 checkout event without creating caller-priced orders.</summary>
public static class BasketCheckedOutHandler
{
    /// <summary>Accepts legacy delivery while V2 remains the sole order-creation path.</summary>
    /// <param name="integrationEvent">The frozen legacy checkout event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public static Task Handle(BasketCheckedOutIntegrationEvent integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return Task.CompletedTask;
    }

    /// <summary>Preserves the previous callable shape while rejecting V1 as a creation source.</summary>
    /// <param name="integrationEvent">The frozen legacy checkout event.</param>
    /// <param name="bus">The message bus, intentionally unused.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A completed task without creating an order.</returns>
    public static Task Handle(BasketCheckedOutIntegrationEvent integrationEvent, IMessageBus bus, CancellationToken ct) => Handle(integrationEvent, ct);
}

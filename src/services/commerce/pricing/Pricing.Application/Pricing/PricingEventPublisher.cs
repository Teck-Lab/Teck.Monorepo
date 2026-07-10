using Pricing.Domain.DomainEvents;
using SharedKernel.Events;
using Wolverine;

namespace Pricing.Application.Pricing;

/// <summary>Publishes <see cref="PriceChanged"/> domain events as integration events after commit.</summary>
public static class PricingEventPublisher
{
    /// <summary>Maps and publishes each price-changed event.</summary>
    /// <param name="events">The captured domain events.</param>
    /// <param name="bus">The message bus.</param>
    /// <returns>A task representing the publish operations.</returns>
    public static async Task PublishAsync(IEnumerable<PriceChanged> events, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(bus);

        foreach (PriceChanged evt in events)
        {
            await bus.PublishAsync(new PriceChangedIntegrationEvent
            {
                ProductId = evt.ProductId,
                PriceListId = evt.PriceListId,
                TenantId = evt.TenantId,
                Amount = evt.Amount,
                Currency = evt.Currency,
                EffectiveFrom = evt.EffectiveFrom,
                ChangeType = evt.ChangeType.ToString(),
            }).ConfigureAwait(false);
        }
    }
}

using Baskets.Application.Baskets.ReadModels;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;

namespace Baskets.Application.Baskets.EventHandlers.IntegrationEvents;

/// <summary>Records a shopper-safe authoritative pricing failure.</summary>
public static class BasketPricingFailedHandler
{
    /// <summary>Transitions the matching pricing-pending basket to checkout failed once.</summary>
    /// <param name="integrationEvent">The structured pricing failure.</param>
    /// <param name="baskets">The tracked basket repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the failure transition.</returns>
    public static async Task Handle(
        BasketPricingFailedIntegrationEvent integrationEvent,
        IGenericWriteRepository<Basket, Guid> baskets,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await baskets
            .FirstOrDefaultAsync(new BasketByIdSpec(integrationEvent.BasketId), enableTracking: true, ct)
            .ConfigureAwait(false);
        if (basket is null || basket.Status != BasketStatus.PricingPending || basket.CheckoutRequestId != integrationEvent.RequestId ||
            !string.Equals(basket.TenantId, integrationEvent.TenantId, StringComparison.Ordinal))
        {
            return;
        }

        basket.FailCheckout(integrationEvent.FailureCategory);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

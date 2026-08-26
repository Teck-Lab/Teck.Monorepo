using Baskets.Application.Baskets.ReadModels;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.FeatureFlags;
using Wolverine;

namespace Baskets.Application.Baskets.EventHandlers.IntegrationEvents;

/// <summary>Completes a pending checkout only from an authoritative pricing event.</summary>
public static class BasketPricedHandler
{
    /// <summary>Persists platform prices and publishes V2 checkout only after the commit succeeds.</summary>
    /// <param name="integrationEvent">The authoritative pricing result.</param>
    /// <param name="baskets">The tracked basket repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="features">The lifecycle feature flag provider.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the completion work.</returns>
    public static async Task Handle(
        BasketPricedIntegrationEvent integrationEvent,
        IGenericWriteRepository<Basket, Guid> baskets,
        IUnitOfWork unitOfWork,
        IFeatureProvider features,
        IMessageBus bus,
        CancellationToken ct)
    {
        var basket = await baskets
            .FirstOrDefaultAsync(new BasketByIdSpec(integrationEvent.BasketId), enableTracking: true, ct)
            .ConfigureAwait(false);
        if (basket is null || basket.Status != BasketStatus.PricingPending || basket.CheckoutRequestId != integrationEvent.RequestId)
        {
            return;
        }

        if (!string.Equals(basket.TenantId, integrationEvent.TenantId, StringComparison.Ordinal) ||
            !string.Equals(basket.Currency, integrationEvent.Currency, StringComparison.OrdinalIgnoreCase) ||
            integrationEvent.Amount > basket.AuthorizedAmount ||
            integrationEvent.AuthorizedAmount != basket.AuthorizedAmount)
        {
            basket.FailCheckout("invalid-pricing-result");
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var pricedItems = integrationEvent.Lines.Select(line =>
        {
            var existing = basket.Items.SingleOrDefault(item => item.ProductId == line.ProductId);
            return new BasketItem(line.ProductId, existing?.ProductName ?? string.Empty, line.UnitPrice, line.Quantity);
        }).ToList();

        try
        {
            basket.ApplyAuthoritativePricing(pricedItems, integrationEvent.Amount, integrationEvent.Currency);
        }
        catch (InvalidOperationException)
        {
            basket.FailCheckout("invalid-pricing-result");
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        if (!features.IsEnabled("CheckoutLifecycleV2"))
        {
            return;
        }

        await bus.PublishAsync(new BasketCheckedOutV2IntegrationEvent
        {
            BasketId = basket.Id,
            KeycloakSubjectId = basket.Subject ?? string.Empty,
            TenantId = basket.TenantId,
            Amount = basket.Subtotal,
            AuthorizedAmount = basket.AuthorizedAmount,
            Currency = basket.Currency!,
            PaymentMethodToken = basket.PaymentReference!,
            SourceCorrelationId = integrationEvent.SourceCorrelationId,
            CheckedOutAt = DateTimeOffset.UtcNow,
            Items = basket.Items.Select(item => new BasketCheckedOutLineV2
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
                LineTotal = item.LineTotal,
            }).ToList(),
        }).ConfigureAwait(false);
    }
}

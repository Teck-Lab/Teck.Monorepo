using Microsoft.Extensions.Options;
using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Pricing.Application.Pricing.EventHandlers.IntegrationEvents;

/// <summary>Performs the same authoritative resolver and ceiling check for a filled backorder.</summary>
public static class BackorderPriceCheckRequestedHandler
{
    /// <summary>Publishes a structured backorder price-check outcome.</summary>
    /// <param name="integrationEvent">The backorder price-check request.</param>
    /// <param name="prices">The active price-list repository.</param>
    /// <param name="exchangeRates">The exchange-rate repository.</param>
    /// <param name="catalogPrices">The catalog fallback repository.</param>
    /// <param name="options">The pricing options.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the price check.</returns>
    public static async Task Handle(
        BackorderPriceCheckRequestedIntegrationEvent integrationEvent,
        IGenericReadRepository<Price, Guid> prices,
        IGenericReadRepository<ExchangeRate, Guid> exchangeRates,
        IGenericReadRepository<CatalogPrice, Guid> catalogPrices,
        IOptions<PricingOptions> options,
        IMessageBus bus,
        CancellationToken ct)
    {
        decimal amount = 0;
        string? failure = null;
        foreach (var line in integrationEvent.Lines)
        {
            var result = await ResolvePriceHandler.ResolveAsync(
                new ResolvePriceQuery(line.ProductId, integrationEvent.Currency, line.Quantity, null, null, null, DateTimeOffset.UtcNow),
                prices,
                exchangeRates,
                catalogPrices,
                options,
                ct).ConfigureAwait(false);
            if (result.IsError)
            {
                failure = result.FirstError.Type == ErrorOr.ErrorType.NotFound ? "price-unavailable" : "currency-mismatch";
                break;
            }

            amount += result.Value.UnitAmount * line.Quantity;
        }

        if (integrationEvent.AuthorizedAmount <= 0 || integrationEvent.Currency.Length != 3)
        {
            failure = "invalid-authorization";
        }
        else if (failure is null && amount > integrationEvent.AuthorizedAmount)
        {
            failure = "authorization-exceeded";
        }

        await bus.PublishAsync(new BackorderPriceCheckedIntegrationEvent
        {
            OrderId = integrationEvent.OrderId,
            BasketId = integrationEvent.BasketId,
            TenantId = integrationEvent.TenantId,
            Amount = amount,
            AuthorizedAmount = integrationEvent.AuthorizedAmount,
            Currency = integrationEvent.Currency,
            IsWithinAuthorizedAmount = failure is null,
            FailureCategory = failure ?? string.Empty,
            SourceCorrelationId = integrationEvent.SourceCorrelationId,
            RequestId = integrationEvent.RequestId,
        }).ConfigureAwait(false);
    }
}

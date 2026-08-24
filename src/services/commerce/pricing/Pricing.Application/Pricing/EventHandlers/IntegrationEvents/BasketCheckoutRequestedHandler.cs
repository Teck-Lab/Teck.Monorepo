using System.Text.Json;
using Microsoft.Extensions.Options;
using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Pricing.Application.Pricing.EventHandlers.IntegrationEvents;

/// <summary>Resolves authoritative platform prices for an asynchronously requested basket checkout.</summary>
public static class BasketCheckoutRequestedHandler
{
    /// <summary>Prices the basket or starts one bounded catalog fallback reconciliation.</summary>
    /// <param name="integrationEvent">The checkout pricing request.</param>
    /// <param name="prices">The active price-list repository.</param>
    /// <param name="exchangeRates">The exchange-rate repository.</param>
    /// <param name="catalogPrices">The catalog fallback repository.</param>
    /// <param name="pendingResolutions">The tracked pending-resolution repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="options">The pricing options.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the pricing work.</returns>
    public static async Task Handle(
        BasketCheckoutRequestedIntegrationEvent integrationEvent,
        IGenericReadRepository<Price, Guid> prices,
        IGenericReadRepository<ExchangeRate, Guid> exchangeRates,
        IGenericReadRepository<CatalogPrice, Guid> catalogPrices,
        IGenericWriteRepository<PendingPriceResolution, Guid> pendingResolutions,
        IUnitOfWork unitOfWork,
        IOptions<PricingOptions> options,
        IMessageBus bus,
        CancellationToken ct)
    {
        var resolution = await TryPriceAsync(integrationEvent, prices, exchangeRates, catalogPrices, options, fallbackOverride: null, ct)
            .ConfigureAwait(false);
        if (resolution.FailureCategory is not null)
        {
            await bus.PublishAsync(Failed(integrationEvent, resolution.FailureCategory)).ConfigureAwait(false);
            return;
        }

        if (resolution.MissingProductId is Guid missingProductId)
        {
            var existing = await pendingResolutions
                .FirstOrDefaultAsync(new PendingPriceResolutionByRequestSpec(integrationEvent.RequestId, integrationEvent.TenantId), enableTracking: true, ct)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return;
            }

            var pending = PendingPriceResolution.Create(
                missingProductId,
                integrationEvent.BasketId,
                integrationEvent.AuthorizedAmount,
                integrationEvent.Currency,
                integrationEvent.RequestId,
                integrationEvent.SourceCorrelationId,
                JsonSerializer.Serialize(integrationEvent.Lines),
                integrationEvent.TenantId);
            await pendingResolutions.AddAsync(pending, ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            await bus.PublishAsync(new CatalogPriceReconciliationRequestedIntegrationEvent
            {
                ProductId = missingProductId,
                TenantId = integrationEvent.TenantId,
                RequestId = integrationEvent.RequestId,
                SourceCorrelationId = integrationEvent.SourceCorrelationId,
            }).ConfigureAwait(false);
            return;
        }

        await bus.PublishAsync(Priced(integrationEvent, resolution.Lines!)).ConfigureAwait(false);
    }

    /// <summary>Resolves every line from price lists first and the catalog fallback second.</summary>
    internal static async Task<(List<BasketPricedLine>? Lines, Guid? MissingProductId, string? FailureCategory)> TryPriceAsync(
        BasketCheckoutRequestedIntegrationEvent integrationEvent,
        IGenericReadRepository<Price, Guid> prices,
        IGenericReadRepository<ExchangeRate, Guid> exchangeRates,
        IGenericReadRepository<CatalogPrice, Guid> catalogPrices,
        IOptions<PricingOptions> options,
        CatalogPrice? fallbackOverride,
        CancellationToken ct)
    {
        if (integrationEvent.AuthorizedAmount <= 0 || integrationEvent.Currency.Length != 3 || integrationEvent.Lines.Count == 0)
        {
            return (null, null, "invalid-checkout");
        }

        var resolvedLines = new List<BasketPricedLine>(integrationEvent.Lines.Count);
        foreach (var line in integrationEvent.Lines)
        {
            var result = await ResolvePriceHandler.ResolveAsync(
                new ResolvePriceQuery(line.ProductId, integrationEvent.Currency, line.Quantity, null, null, null, DateTimeOffset.UtcNow),
                prices,
                exchangeRates,
                catalogPrices,
                options,
                ct).ConfigureAwait(false);

            if (result.IsError && fallbackOverride is not null && fallbackOverride.ProductId == line.ProductId)
            {
                if (!string.Equals(fallbackOverride.Currency, integrationEvent.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    return (null, null, "currency-mismatch");
                }

                result = new global::Pricing.Application.Pricing.Responses.ResolvedPriceDto(
                    line.ProductId,
                    fallbackOverride.Amount,
                    fallbackOverride.Currency,
                    Guid.Empty,
                    Converted: false,
                    RateApplied: null);
            }

            if (result.IsError)
            {
                return result.FirstError.Type == ErrorOr.ErrorType.NotFound
                    ? (null, line.ProductId, null)
                    : (null, null, "currency-mismatch");
            }

            decimal lineTotal = result.Value.UnitAmount * line.Quantity;
            resolvedLines.Add(new BasketPricedLine
            {
                ProductId = line.ProductId,
                UnitPrice = result.Value.UnitAmount,
                Quantity = line.Quantity,
                LineTotal = lineTotal,
            });
        }

        decimal total = resolvedLines.Sum(line => line.LineTotal);
        return total > integrationEvent.AuthorizedAmount
            ? (null, null, "authorization-exceeded")
            : (resolvedLines, null, null);
    }

    /// <summary>Creates the authoritative pricing success event.</summary>
    internal static BasketPricedIntegrationEvent Priced(BasketCheckoutRequestedIntegrationEvent integrationEvent, List<BasketPricedLine> lines) => new()
    {
        BasketId = integrationEvent.BasketId,
        TenantId = integrationEvent.TenantId,
        Amount = lines.Sum(line => line.LineTotal),
        AuthorizedAmount = integrationEvent.AuthorizedAmount,
        Currency = integrationEvent.Currency,
        RequestId = integrationEvent.RequestId,
        SourceCorrelationId = integrationEvent.SourceCorrelationId,
        Lines = lines,
    };

    /// <summary>Creates a shopper-safe pricing failure event.</summary>
    internal static BasketPricingFailedIntegrationEvent Failed(BasketCheckoutRequestedIntegrationEvent integrationEvent, string category) => new()
    {
        BasketId = integrationEvent.BasketId,
        TenantId = integrationEvent.TenantId,
        RequestId = integrationEvent.RequestId,
        SourceCorrelationId = integrationEvent.SourceCorrelationId,
        FailureCategory = category,
        ActionText = "Review your basket and checkout authorization before trying again.",
    };
}

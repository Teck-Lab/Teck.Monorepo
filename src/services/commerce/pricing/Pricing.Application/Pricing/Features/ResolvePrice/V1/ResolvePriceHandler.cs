using ErrorOr;
using Microsoft.Extensions.Options;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.Services;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.ResolvePrice.V1;

/// <summary>Handles <see cref="ResolvePriceQuery"/>: selects the best price and applies FX when needed.</summary>
public static class ResolvePriceHandler
{
    /// <summary>Resolves the effective price, converting cross-currency winners via a stored rate.</summary>
    /// <param name="query">The query.</param>
    /// <param name="prices">The price read repository.</param>
    /// <param name="rates">The exchange-rate read repository.</param>
    /// <param name="options">The pricing options (rounding).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The resolved price, a not-found error, or a failure when no conversion rate exists.</returns>
    public static async Task<ErrorOr<ResolvedPriceDto>> Handle(
        ResolvePriceQuery query,
        IGenericReadRepository<Price, Guid> prices,
        IGenericReadRepository<ExchangeRate, Guid> rates,
        IOptions<PricingOptions> options,
        CancellationToken ct)
        => await ResolveAsync(query, prices, rates, catalogPrices: null, options, ct).ConfigureAwait(false);

    /// <summary>Resolves a price list first, then the tenant-scoped catalog fallback projection.</summary>
    /// <param name="query">The requested price context.</param>
    /// <param name="prices">The active price-list repository.</param>
    /// <param name="rates">The exchange-rate repository.</param>
    /// <param name="catalogPrices">The optional catalog fallback projection repository.</param>
    /// <param name="options">The pricing options.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The authoritative resolved price, or a structured error.</returns>
    public static async Task<ErrorOr<ResolvedPriceDto>> ResolveAsync(
        ResolvePriceQuery query,
        IGenericReadRepository<Price, Guid> prices,
        IGenericReadRepository<ExchangeRate, Guid> rates,
        IGenericReadRepository<CatalogPrice, Guid>? catalogPrices,
        IOptions<PricingOptions> options,
        CancellationToken ct)
    {
        DateTimeOffset at = query.At ?? DateTimeOffset.UtcNow;
        var context = new PriceResolutionContext(query.Currency, query.Quantity, query.Country, query.CustomerGroupId, query.ChannelId, at);

        IReadOnlyList<Price> candidates = await prices.ListAsync(new PricesByProductSpec(query.ProductId), ct).ConfigureAwait(false);

        ResolvedSelection? selection = PriceResolutionService.SelectBest(candidates, context);
        if (selection is null)
        {
            if (catalogPrices is not null)
            {
                CatalogPrice? fallback = await catalogPrices
                    .FirstOrDefaultAsync(new CatalogPriceByProductSpec(query.ProductId), ct)
                    .ConfigureAwait(false);
                if (fallback is not null)
                {
                    if (!string.Equals(fallback.Currency, query.Currency, StringComparison.OrdinalIgnoreCase))
                    {
                        return Error.Failure(description: $"Catalog fallback currency '{fallback.Currency}' does not match '{query.Currency}'.");
                    }

                    return new ResolvedPriceDto(query.ProductId, fallback.Amount, fallback.Currency, Guid.Empty, Converted: false, RateApplied: null);
                }
            }

            return Error.NotFound(description: $"No applicable price for product '{query.ProductId}' in '{query.Currency}'.");
        }

        Money unit = selection.UnitAmount;
        if (string.Equals(unit.Currency, query.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedPriceDto(query.ProductId, unit.Amount, query.Currency, selection.Price.PriceListId, Converted: false, RateApplied: null);
        }

        ExchangeRate? rate = await rates.FirstOrDefaultAsync(new ExchangeRateByPairSpec(unit.Currency, query.Currency), ct).ConfigureAwait(false);
        if (rate is null || !rate.IsValidAt(at))
        {
            return Error.Failure(description: $"No conversion rate from '{unit.Currency}' to '{query.Currency}'.");
        }

        PricingOptions opts = options.Value;
        Money converted = CurrencyConverter.Convert(unit, query.Currency, rate.Rate, opts.RoundingDecimals, opts.RoundingMode);
        return new ResolvedPriceDto(query.ProductId, converted.Amount, query.Currency, selection.Price.PriceListId, Converted: true, RateApplied: rate.Rate);
    }
}

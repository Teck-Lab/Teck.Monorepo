namespace Pricing.Application.Pricing.Responses;

/// <summary>The resolved price for a product in a request context.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="UnitAmount">The resolved unit amount (converted if cross-currency).</param>
/// <param name="Currency">The requested ISO currency of the amount.</param>
/// <param name="PriceListId">The winning price list.</param>
/// <param name="Converted">Whether an FX conversion was applied.</param>
/// <param name="RateApplied">The FX rate applied, or null when native.</param>
public sealed record ResolvedPriceDto(
    Guid ProductId,
    decimal UnitAmount,
    string Currency,
    Guid PriceListId,
    bool Converted,
    decimal? RateApplied);

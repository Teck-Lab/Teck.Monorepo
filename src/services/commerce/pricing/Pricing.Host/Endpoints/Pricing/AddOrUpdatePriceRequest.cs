using Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to add or update a product's price within a list.</summary>
/// <param name="Id">The owning price list identifier.</param>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Amount">The base unit amount (in the list's currency).</param>
/// <param name="Tiers">The quantity tiers.</param>
public sealed record AddOrUpdatePriceRequest(
    Guid Id,
    Guid ProductId,
    decimal Amount,
    IReadOnlyList<PriceTierInput> Tiers);

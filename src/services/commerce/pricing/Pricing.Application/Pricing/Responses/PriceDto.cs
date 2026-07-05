namespace Pricing.Application.Pricing.Responses;

/// <summary>A product price in API responses.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Amount">The base unit amount.</param>
/// <param name="Currency">The ISO currency.</param>
/// <param name="Tiers">The quantity tiers.</param>
public sealed record PriceDto(Guid ProductId, decimal Amount, string Currency, IReadOnlyList<PriceTierDto> Tiers);

namespace Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;

/// <summary>A quantity tier supplied on a price command (currency is the list's scope currency).</summary>
/// <param name="MinQuantity">The minimum quantity (>= 1).</param>
/// <param name="Amount">The unit amount.</param>
public sealed record PriceTierInput(int MinQuantity, decimal Amount);

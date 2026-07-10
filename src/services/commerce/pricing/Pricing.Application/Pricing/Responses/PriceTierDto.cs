namespace Pricing.Application.Pricing.Responses;

/// <summary>A quantity tier in API responses.</summary>
/// <param name="MinQuantity">The minimum quantity at which the amount applies.</param>
/// <param name="Amount">The unit amount.</param>
public sealed record PriceTierDto(int MinQuantity, decimal Amount);

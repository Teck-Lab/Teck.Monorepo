namespace Pricing.Domain.ValueObjects;

/// <summary>A quantity tier within a price: the unit amount that applies from a minimum quantity.</summary>
/// <param name="MinQuantity">The minimum quantity (>= 1) at which this tier's amount applies.</param>
/// <param name="Amount">The unit amount for this tier.</param>
public sealed record PriceTier(int MinQuantity, Money Amount);

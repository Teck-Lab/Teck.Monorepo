namespace Pricing.Application.Pricing;

/// <summary>Configuration options for the pricing service.</summary>
public sealed class PricingOptions
{
    /// <summary>Gets the number of decimals FX conversion rounds to.</summary>
    public int RoundingDecimals { get; init; } = 2;

    /// <summary>Gets the midpoint rounding mode used for FX conversion.</summary>
    public MidpointRounding RoundingMode { get; init; } = MidpointRounding.ToEven;

    /// <summary>Gets the maximum number of tiers allowed on a single price.</summary>
    public int MaxTiersPerPrice { get; init; } = 20;
}

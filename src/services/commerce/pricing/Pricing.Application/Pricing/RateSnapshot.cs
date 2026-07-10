namespace Pricing.Application.Pricing;

/// <summary>A rate observation from an external provider.</summary>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
/// <param name="Rate">The multiplicative rate.</param>
public sealed record RateSnapshot(string FromCurrency, string ToCurrency, decimal Rate);

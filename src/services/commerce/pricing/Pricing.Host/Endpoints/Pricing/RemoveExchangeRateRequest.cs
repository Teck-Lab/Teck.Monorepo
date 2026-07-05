namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to remove the exchange rate for a currency pair.</summary>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
public sealed record RemoveExchangeRateRequest(string FromCurrency, string ToCurrency);

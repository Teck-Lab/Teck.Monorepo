namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to create or update the exchange rate for a currency pair.</summary>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
/// <param name="Rate">The positive multiplicative rate.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
public sealed record SetExchangeRateRequest(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil);

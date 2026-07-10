namespace Pricing.Application.Pricing.Responses;

/// <summary>An exchange rate in API responses.</summary>
/// <param name="Id">The rate identifier.</param>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
/// <param name="Rate">The multiplicative rate.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
public sealed record ExchangeRateDto(
    Guid Id,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil);

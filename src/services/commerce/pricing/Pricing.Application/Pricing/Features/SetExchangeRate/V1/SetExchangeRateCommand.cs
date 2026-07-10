using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.SetExchangeRate.V1;

/// <summary>Command that creates or updates the exchange rate for a currency pair.</summary>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
/// <param name="Rate">The positive multiplicative rate.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
public sealed record SetExchangeRateCommand(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil) : ICommand<ExchangeRateDto>;

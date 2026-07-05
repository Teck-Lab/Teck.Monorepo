using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.RemoveExchangeRate.V1;

/// <summary>Command that removes the exchange rate for a currency pair.</summary>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
public sealed record RemoveExchangeRateCommand(string FromCurrency, string ToCurrency) : ICommand<ErrorOr<Success>>;

using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects all exchange rates ordered by currency pair.</summary>
public sealed class AllExchangeRatesSpec : Specification<ExchangeRate>
{
    /// <summary>Initializes a new instance of the <see cref="AllExchangeRatesSpec"/> class.</summary>
    public AllExchangeRatesSpec() =>
        Query.OrderBy(rate => rate.FromCurrency).ThenBy(rate => rate.ToCurrency);
}

using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects the exchange rate for a currency pair.</summary>
public sealed class ExchangeRateByPairSpec : Specification<ExchangeRate>
{
    /// <summary>Initializes a new instance of the <see cref="ExchangeRateByPairSpec"/> class.</summary>
    /// <param name="from">The source ISO currency.</param>
    /// <param name="to">The target ISO currency.</param>
    public ExchangeRateByPairSpec(string from, string to) =>
        Query.Where(rate => rate.FromCurrency == from && rate.ToCurrency == to);
}

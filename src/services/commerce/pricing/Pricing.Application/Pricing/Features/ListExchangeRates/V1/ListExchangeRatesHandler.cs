using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.ListExchangeRates.V1;

/// <summary>Handles <see cref="ListExchangeRatesQuery"/>.</summary>
public static class ListExchangeRatesHandler
{
    /// <summary>Lists all exchange rates mapped to DTOs.</summary>
    /// <param name="query">The query.</param>
    /// <param name="repository">The read repository.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The exchange rates.</returns>
    public static async Task<IReadOnlyList<ExchangeRateDto>> Handle(
        ListExchangeRatesQuery query,
        IGenericReadRepository<ExchangeRate, Guid> repository,
        CancellationToken ct)
    {
        IReadOnlyList<ExchangeRate> rates = await repository.ListAsync(new AllExchangeRatesSpec(), ct).ConfigureAwait(false);
        return rates.Select(rate => rate.ToDto()).ToList();
    }
}

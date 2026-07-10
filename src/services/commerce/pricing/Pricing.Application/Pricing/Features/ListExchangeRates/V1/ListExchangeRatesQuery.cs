using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ListExchangeRates.V1;

/// <summary>Query that lists all exchange rates for the tenant.</summary>
public sealed record ListExchangeRatesQuery : IQuery<IReadOnlyList<ExchangeRateDto>>;

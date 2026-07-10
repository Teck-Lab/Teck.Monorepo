using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Pricing.Application.Pricing.Mapping;

/// <summary>Mapperly mappings for exchange rates.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class ExchangeRateMapper
{
    /// <summary>Maps an <see cref="ExchangeRate"/> to an <see cref="ExchangeRateDto"/>.</summary>
    /// <param name="rate">The exchange rate.</param>
    /// <returns>The mapped DTO.</returns>
    public static partial ExchangeRateDto ToDto(this ExchangeRate rate);
}

using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using Riok.Mapperly.Abstractions;

namespace Pricing.Application.Pricing.Mapping;

/// <summary>Mapperly mappings from pricing entities to their DTOs.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class PriceListMapper
{
    /// <summary>Maps a <see cref="PriceList"/> to a <see cref="PriceListDto"/>.</summary>
    /// <param name="list">The price list.</param>
    /// <returns>The mapped DTO.</returns>
    [MapProperty("Status.Name", nameof(PriceListDto.Status))]
    [MapProperty("Scope.Currency", nameof(PriceListDto.Currency))]
    [MapProperty("Scope.Country", nameof(PriceListDto.Country))]
    [MapProperty("Scope.CustomerGroupId", nameof(PriceListDto.CustomerGroupId))]
    [MapProperty("Scope.ChannelId", nameof(PriceListDto.ChannelId))]
    public static partial PriceListDto ToDto(this PriceList list);

    /// <summary>Maps a <see cref="Price"/> to a <see cref="PriceDto"/>.</summary>
    /// <param name="price">The price.</param>
    /// <returns>The mapped DTO.</returns>
    [MapProperty("Amount.Amount", nameof(PriceDto.Amount))]
    [MapProperty("Amount.Currency", nameof(PriceDto.Currency))]
    public static partial PriceDto ToDto(this Price price);

    /// <summary>Maps a <see cref="PriceTier"/> to a <see cref="PriceTierDto"/>.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The mapped DTO.</returns>
    [MapProperty("Amount.Amount", nameof(PriceTierDto.Amount))]
    public static partial PriceTierDto ToDto(this PriceTier tier);
}

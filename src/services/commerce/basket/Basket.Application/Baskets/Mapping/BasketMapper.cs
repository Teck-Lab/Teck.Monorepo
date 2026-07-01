using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Baskets.Application.Baskets.Mapping;

/// <summary>Mapperly-generated mappings between basket entities and their DTOs.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class BasketMapper
{
    /// <summary>Maps a <see cref="Basket"/> entity to a <see cref="BasketDto"/>.</summary>
    /// <param name="entity">The basket entity to map.</param>
    /// <returns>The mapped basket response.</returns>
    [MapProperty("Status.Name", nameof(BasketDto.Status))]
    public static partial BasketDto ToDto(this Basket entity);

    /// <summary>Maps a basket item value object to its DTO.</summary>
    /// <param name="item">The item to map.</param>
    /// <returns>The mapped item DTO.</returns>
    public static partial BasketItemDto ToDto(this global::Baskets.Domain.ValueObjects.BasketItem item);
}

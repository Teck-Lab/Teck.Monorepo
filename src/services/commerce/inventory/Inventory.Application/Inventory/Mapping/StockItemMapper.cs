using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Inventories.Application.Inventory.Mapping;

/// <summary>Mapperly-generated mappings between stock entities and their DTOs.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class StockItemMapper
{
    /// <summary>Maps a <see cref="StockItem"/> entity to a <see cref="StockItemDto"/>.</summary>
    /// <param name="entity">The stock item entity to map.</param>
    /// <returns>The mapped stock item response.</returns>
    [MapProperty(nameof(StockItem.QuantityOnHand), nameof(StockItemDto.OnHand))]
    [MapProperty(nameof(StockItem.QuantityReserved), nameof(StockItemDto.Reserved))]
    public static partial StockItemDto ToDto(this StockItem entity);
}

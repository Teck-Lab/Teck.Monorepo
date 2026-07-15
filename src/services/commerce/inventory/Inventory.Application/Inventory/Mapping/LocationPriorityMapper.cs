using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Inventories.Application.Inventory.Mapping;

/// <summary>Mapperly-generated mappings between location priority entities and their DTOs.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class LocationPriorityMapper
{
    /// <summary>Maps a <see cref="LocationPriority"/> entity to a <see cref="LocationPriorityDto"/>.</summary>
    /// <param name="entity">The location priority entity to map.</param>
    /// <returns>The mapped location priority response.</returns>
    public static partial LocationPriorityDto ToDto(this LocationPriority entity);
}

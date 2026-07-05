namespace Inventories.Application.Inventory.Responses;

/// <summary>Represents the aggregate and per-location availability for a product in API responses.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Available">The total quantity available to promise across all locations.</param>
/// <param name="ByLocation">The availability broken down by location.</param>
public sealed record AvailabilityDto(Guid ProductId, int Available, IReadOnlyList<LocationAvailabilityDto> ByLocation);

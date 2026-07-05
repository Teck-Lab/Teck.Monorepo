namespace Inventories.Application.Inventory.Responses;

/// <summary>Represents the available quantity for a product at a single location.</summary>
/// <param name="LocationId">The location identifier.</param>
/// <param name="Available">The quantity available to promise at the location.</param>
public sealed record LocationAvailabilityDto(Guid LocationId, int Available);

namespace Inventories.Application.Inventory.Responses;

/// <summary>Represents a tenant's ordered stock-location allocation priorities in API responses.</summary>
/// <param name="Id">The location priority list identifier.</param>
/// <param name="LocationIds">The location identifiers in descending allocation priority order.</param>
public sealed record LocationPriorityDto(Guid Id, IReadOnlyList<Guid> LocationIds);

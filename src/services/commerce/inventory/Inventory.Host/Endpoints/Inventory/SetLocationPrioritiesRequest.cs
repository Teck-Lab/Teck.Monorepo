namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Request to set a tenant's ordered stock-location allocation priorities.</summary>
/// <param name="LocationIds">The location identifiers in descending allocation priority order.</param>
public sealed record SetLocationPrioritiesRequest(IReadOnlyList<Guid> LocationIds);

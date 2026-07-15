namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Request to retrieve availability for a product, optionally scoped to a single location.</summary>
/// <param name="ProductId">The product identifier (bound from query string).</param>
/// <param name="LocationId">An optional location identifier that, when supplied, restricts the result to that single location (bound from query string).</param>
public sealed record GetAvailabilityRequest(Guid ProductId, Guid? LocationId);

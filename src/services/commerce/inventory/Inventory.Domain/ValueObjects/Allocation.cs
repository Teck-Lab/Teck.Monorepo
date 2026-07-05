namespace Inventories.Domain.ValueObjects;

/// <summary>
/// Represents the quantity of a <see cref="ReservationLine"/> allocated from a single stock location.
/// </summary>
/// <param name="LocationId">The identifier of the location the quantity is allocated from.</param>
/// <param name="Quantity">The quantity allocated from the location.</param>
public sealed record Allocation(Guid LocationId, int Quantity);

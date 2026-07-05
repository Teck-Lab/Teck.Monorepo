namespace Inventories.Domain.ValueObjects;

/// <summary>
/// Represents a single product line within a <see cref="Entities.Reservation"/> and how its
/// requested quantity was allocated across locations.
/// </summary>
/// <param name="ProductId">The identifier of the reserved product.</param>
/// <param name="RequestedQuantity">The quantity originally requested for this product.</param>
/// <param name="BackorderedQuantity">The portion of the requested quantity that could not be allocated from on-hand stock.</param>
/// <param name="Allocations">The per-location allocations that satisfy this line.</param>
public sealed record ReservationLine(
    Guid ProductId,
    int RequestedQuantity,
    int BackorderedQuantity,
    IReadOnlyList<Allocation> Allocations);

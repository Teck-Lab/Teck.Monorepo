namespace Inventories.Application.Inventory.Features.CommitReservation.V1;

/// <summary>A single requested product line to be committed by the <see cref="ReservationCommitter"/>.</summary>
/// <param name="ProductId">The identifier of the product to reserve.</param>
/// <param name="Quantity">The quantity requested for the product.</param>
internal sealed record ReservationRequestLine(Guid ProductId, int Quantity);

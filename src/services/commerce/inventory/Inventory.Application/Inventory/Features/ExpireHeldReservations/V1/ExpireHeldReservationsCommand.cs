using SharedKernel.Core.CQRS;

namespace Inventories.Application.Inventory.Features.ExpireHeldReservations.V1;

/// <summary>
/// Command that sweeps <see cref="Inventories.Domain.ValueObjects.ReservationStatus.Held"/>
/// reservations whose hold has lapsed, transitioning each to
/// <see cref="Inventories.Domain.ValueObjects.ReservationStatus.Expired"/> and releasing its
/// allocations. Invoked periodically by <c>ReservationExpirySweepService</c>; carries no tenant —
/// it is intentionally cross-tenant, one run per sweep interval for every tenant.
/// </summary>
public sealed record ExpireHeldReservationsCommand : ICommand<int>;

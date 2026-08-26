using SharedKernel.Core.CQRS;

namespace Inventories.Application.Inventory.Features.ExpireHeldReservations.V1;

/// <summary>
/// Command that sweeps <see cref="Inventories.Domain.ValueObjects.ReservationStatus.Held"/>
/// reservations whose hold has lapsed for one established tenant, transitioning each to
/// <see cref="Inventories.Domain.ValueObjects.ReservationStatus.Expired"/> and releasing its
/// allocations. The hosted sweep discovers eligible tenant ids without mutating data, then invokes
/// this command once in each tenant scope.
/// </summary>
public sealed record ExpireHeldReservationsCommand(string TenantId) : ICommand<int>;

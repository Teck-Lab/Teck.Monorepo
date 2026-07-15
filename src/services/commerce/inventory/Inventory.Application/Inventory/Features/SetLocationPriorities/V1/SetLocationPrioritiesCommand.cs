using Inventories.Application.Inventory.Responses;
using SharedKernel.Core.CQRS;

namespace Inventories.Application.Inventory.Features.SetLocationPriorities.V1;

/// <summary>Command that sets or replaces a tenant's ordered stock-location allocation priorities.</summary>
/// <param name="LocationIds">The location identifiers in descending allocation priority order.</param>
public sealed record SetLocationPrioritiesCommand(IReadOnlyList<Guid> LocationIds) : ICommand<LocationPriorityDto>;

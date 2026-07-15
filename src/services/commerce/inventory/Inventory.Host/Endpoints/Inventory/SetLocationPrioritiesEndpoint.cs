using Inventories.Application.Inventory.Features.SetLocationPriorities.V1;
using Inventories.Application.Inventory.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Sets the tenant's ordered stock-location allocation priorities.</summary>
/// <param name="bus">The message bus.</param>
public sealed class SetLocationPrioritiesEndpoint(IMessageBus bus) : AuthenticatedEndpoint<SetLocationPrioritiesRequest, LocationPriorityDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("inventory", "set-location-priorities", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(SetLocationPrioritiesRequest request, CancellationToken ct)
    {
        var command = new SetLocationPrioritiesCommand(request.LocationIds);
        var result = await bus.InvokeAsync<LocationPriorityDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/inventory/location-priorities");
        Version(0);
    }
}

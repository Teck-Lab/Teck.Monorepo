using Inventories.Application.Inventory.Features.GetAvailability.V1;
using Inventories.Application.Inventory.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Returns the total and per-location availability for a product.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetAvailabilityEndpoint(IMessageBus bus) : AuthenticatedEndpoint<GetAvailabilityRequest, AvailabilityDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("inventory", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetAvailabilityRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<AvailabilityDto>(
            new GetAvailabilityQuery(request.ProductId, request.LocationId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/inventory/availability");
        Version(0);
    }
}

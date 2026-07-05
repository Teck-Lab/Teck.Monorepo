using Inventories.Application.Inventory.Features.SetPolicy.V1;
using Inventories.Application.Inventory.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Updates a stock item's backorder and reorder-threshold policy.</summary>
/// <param name="bus">The message bus.</param>
public sealed class SetPolicyEndpoint(IMessageBus bus) : AuthenticatedEndpoint<SetPolicyRequest, StockItemDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("inventory", "set-policy", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(SetPolicyRequest request, CancellationToken ct)
    {
        var command = new SetPolicyCommand(request.Id, request.AllowBackorder, request.ReorderThreshold);
        var result = await bus.InvokeAsync<StockItemDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/inventory/stock-items/{id}/policy");
        Version(0);
    }
}

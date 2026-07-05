using Inventories.Application.Inventory.Features.AdjustStock.V1;
using Inventories.Application.Inventory.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Adjusts a stock item's quantity on hand by a signed delta.</summary>
/// <param name="bus">The message bus.</param>
public sealed class AdjustStockEndpoint(IMessageBus bus) : AuthenticatedEndpoint<AdjustStockRequest, StockItemDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("inventory", "adjust", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(AdjustStockRequest request, CancellationToken ct)
    {
        var command = new AdjustStockCommand(request.Id, request.Delta);
        var result = await bus.InvokeAsync<StockItemDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/inventory/stock-items/{id}/adjust");
        Version(0);
    }
}

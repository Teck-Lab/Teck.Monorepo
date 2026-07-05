using Inventories.Application.Inventory.Features.ListStockItems.V1;
using Inventories.Application.Inventory.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Lists every stock record for a product across all locations.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ListStockItemsEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<ListStockItemsRequest, IReadOnlyList<StockItemDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("inventory", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(ListStockItemsRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<StockItemDto>>(new ListStockItemsQuery(request.ProductId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/inventory/stock-items");
        Version(0);
    }
}

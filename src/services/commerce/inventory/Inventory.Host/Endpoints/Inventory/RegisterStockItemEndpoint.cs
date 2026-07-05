using Inventories.Application.Inventory.Features.RegisterStockItem.V1;
using Inventories.Application.Inventory.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Registers a new stock item for a product at a location.</summary>
/// <param name="bus">The message bus.</param>
public sealed class RegisterStockItemEndpoint(IMessageBus bus) : AuthenticatedEndpoint<RegisterStockItemRequest, StockItemDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("inventory", "register", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(RegisterStockItemRequest request, CancellationToken ct)
    {
        var command = new RegisterStockItemCommand(
            request.ProductId,
            request.LocationId,
            request.QuantityOnHand,
            request.AllowBackorder,
            request.ReorderThreshold);
        var result = await bus.InvokeAsync<StockItemDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/inventory/stock-items/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/inventory/stock-items");
        Version(0);
    }
}

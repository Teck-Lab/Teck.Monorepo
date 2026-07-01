using Baskets.Application.Baskets.Features.AddItem.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Adds an item to a basket.</summary>
/// <param name="bus">The message bus.</param>
public sealed class AddItemEndpoint(IMessageBus bus) : AuthenticatedEndpoint<AddItemRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

    /// <inheritdoc/>
    public override async Task HandleAsync(AddItemRequest request, CancellationToken ct)
    {
        var command = new AddItemCommand(request.BasketId, request.ProductId, request.ProductName, request.UnitPrice, request.Quantity);
        var result = await bus.InvokeAsync<BasketDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/baskets/items");
        Version(0);
    }
}

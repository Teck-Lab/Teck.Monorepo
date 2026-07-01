using Baskets.Application.Baskets.Features.RemoveItem.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Removes an item from a basket.</summary>
/// <param name="bus">The message bus.</param>
public sealed class RemoveItemEndpoint(IMessageBus bus) : AuthenticatedEndpoint<RemoveItemRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

    /// <inheritdoc/>
    public override async Task HandleAsync(RemoveItemRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(
            new RemoveItemCommand(request.BasketId, request.ProductId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Delete("/baskets/items/{productId}");
        Version(0);
    }
}

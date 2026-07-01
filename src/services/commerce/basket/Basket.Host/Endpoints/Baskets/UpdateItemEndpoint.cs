using Baskets.Application.Baskets.Features.UpdateItemQuantity.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Updates the quantity of a basket line.</summary>
/// <param name="bus">The message bus.</param>
public sealed class UpdateItemEndpoint(IMessageBus bus) : AuthenticatedEndpoint<UpdateItemRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

    /// <inheritdoc/>
    public override async Task HandleAsync(UpdateItemRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(
            new UpdateItemQuantityCommand(request.BasketId, request.ProductId, request.Quantity), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/baskets/items/{productId}");
        Version(0);
    }
}

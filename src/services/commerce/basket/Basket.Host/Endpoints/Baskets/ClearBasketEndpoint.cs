using Baskets.Application.Baskets.Features.ClearBasket.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Clears all items from a basket.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ClearBasketEndpoint(IMessageBus bus) : AuthenticatedEndpoint<ClearBasketRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

    /// <inheritdoc/>
    public override async Task HandleAsync(ClearBasketRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(new ClearBasketCommand(request.BasketId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/baskets/clear");
        Version(0);
    }
}

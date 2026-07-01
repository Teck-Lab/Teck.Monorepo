using Baskets.Application.Baskets.Features.Checkout.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Checks out a basket.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CheckoutBasketEndpoint(IMessageBus bus) : AuthenticatedEndpoint<CheckoutBasketRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("basket", "checkout", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CheckoutBasketRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(new CheckoutCommand(request.BasketId), ct);
        HttpContext.Response.Headers.Location = $"/baskets/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/baskets/checkout");
        Version(0);
    }
}

using Baskets.Application.Baskets.Features.GetOrCreateBasket.V1;
using Baskets.Application.Baskets.Responses;
using Baskets.Host.Infrastructure;
using FastEndpoints;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Returns the caller's active basket, creating one if none exists.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetCurrentBasketEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<EmptyRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(new GetOrCreateBasketCommand(), ct);
        if (result.AnonymousToken is Guid token)
        {
            HttpContext.Response.Headers[BasketIdentityAccessor.TokenHeader] = token.ToString();
        }

        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/baskets/current");
        Version(0);
    }
}

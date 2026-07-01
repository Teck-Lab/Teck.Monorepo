using Baskets.Application.Baskets.Features.MergeBasket.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Merges a guest basket into the authenticated customer's basket.</summary>
/// <param name="bus">The message bus.</param>
public sealed class MergeBasketEndpoint(IMessageBus bus) : AuthenticatedEndpoint<MergeBasketRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("basket", "merge", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(MergeBasketRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(new MergeBasketCommand(request.AnonymousToken), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/baskets/merge");
        Version(0);
    }
}

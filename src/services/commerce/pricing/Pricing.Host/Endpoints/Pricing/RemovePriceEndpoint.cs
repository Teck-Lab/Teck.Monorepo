using Pricing.Application.Pricing.Features.RemovePrice.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Removes a product's price from a price list.</summary>
/// <param name="bus">The message bus.</param>
public sealed class RemovePriceEndpoint(IMessageBus bus) : AuthenticatedEndpoint<RemovePriceRequest, PriceListDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(RemovePriceRequest request, CancellationToken ct)
    {
        var command = new RemovePriceCommand(request.Id, request.ProductId);
        var result = await bus.InvokeAsync<PriceListDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Delete("/price-lists/{id}/prices/{productId}");
        Version(0);
    }
}

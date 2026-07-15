using Catalog.Application.Products.Features.UpdateSellPrice.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Changes a variant's sell price.</summary>
/// <param name="bus">The message bus.</param>
public sealed class UpdateSellPriceEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<UpdateSellPriceRequest, VariantDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(UpdateSellPriceRequest request, CancellationToken ct)
    {
        var command = new UpdateSellPriceCommand(request.ProductId, request.VariantId, request.Amount, request.Currency);
        var result = await bus.InvokeAsync<VariantDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/products/{productId}/variants/{variantId}/sell-price");
        Version(0);
    }
}

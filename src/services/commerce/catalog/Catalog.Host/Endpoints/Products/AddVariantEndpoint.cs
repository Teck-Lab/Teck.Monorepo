using Catalog.Application.Products.Features.AddVariant.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Adds a variant to a product.</summary>
/// <param name="bus">The message bus.</param>
public sealed class AddVariantEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<AddVariantRequest, VariantDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(AddVariantRequest request, CancellationToken ct)
    {
        var command = new AddVariantCommand(
            request.ProductId, request.Sku, request.SellPriceAmount, request.SellPriceCurrency, request.Attributes);
        var result = await bus.InvokeAsync<VariantDto>(command, ct);
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/products/{productId}/variants");
        Version(0);
    }
}

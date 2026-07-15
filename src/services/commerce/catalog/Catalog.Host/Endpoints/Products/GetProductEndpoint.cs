using Catalog.Application.Products.Features.GetProduct.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Fetches a product by identifier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetProductEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetProductRequest, ProductDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetProductRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<ProductDto>(new GetProductQuery(request.ProductId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/products/{productId}");
        Version(0);
    }
}

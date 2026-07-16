using Catalog.Application.Products.Features.ListProducts.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Lists products.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ListProductsEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<ListProductsRequest, IReadOnlyList<ProductSummaryDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(ListProductsRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<ProductSummaryDto>>(
            new ListProductsQuery(request.CategoryId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/products");
        Version(0);
    }
}

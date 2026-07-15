using Catalog.Application.Products.Features.CreateProduct.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Creates a product.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreateProductEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CreateProductRequest, ProductDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateProductRequest request, CancellationToken ct)
    {
        var command = new CreateProductCommand(
            request.Name, request.Description, request.CategoryId,
            request.Sku, request.SellPriceAmount, request.SellPriceCurrency);
        var result = await bus.InvokeAsync<ProductDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/products/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/products");
        Version(0);
    }
}

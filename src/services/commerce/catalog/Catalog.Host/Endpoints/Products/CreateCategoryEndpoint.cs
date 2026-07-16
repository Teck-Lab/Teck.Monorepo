using Catalog.Application.Products.Features.CreateCategory.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Creates a category.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreateCategoryEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CreateCategoryRequest, CategoryDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var command = new CreateCategoryCommand(request.Name, request.Slug, request.ParentId);
        var result = await bus.InvokeAsync<CategoryDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/categories/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/categories");
        Version(0);
    }
}

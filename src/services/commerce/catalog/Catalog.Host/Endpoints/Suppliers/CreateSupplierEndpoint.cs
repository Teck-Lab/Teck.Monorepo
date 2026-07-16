using Catalog.Application.Suppliers.Features.CreateSupplier.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Creates a supplier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreateSupplierEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CreateSupplierRequest, SupplierDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateSupplierRequest request, CancellationToken ct)
    {
        var command = new CreateSupplierCommand(request.Name, request.ContactEmail, request.ContactPhone);
        var result = await bus.InvokeAsync<SupplierDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/suppliers/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/suppliers");
        Version(0);
    }
}

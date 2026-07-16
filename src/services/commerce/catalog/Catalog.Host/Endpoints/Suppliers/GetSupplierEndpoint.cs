using Catalog.Application.Suppliers.Features.GetSupplier.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Fetches a supplier by identifier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetSupplierEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetSupplierRequest, SupplierDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetSupplierRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<SupplierDto>(new GetSupplierQuery(request.SupplierId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/suppliers/{supplierId}");
        Version(0);
    }
}

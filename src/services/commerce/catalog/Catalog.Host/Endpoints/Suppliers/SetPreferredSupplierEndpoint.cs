using Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;
using ErrorOr;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Sets the preferred supplier for a variant.</summary>
/// <param name="bus">The message bus.</param>
public sealed class SetPreferredSupplierEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<SetPreferredSupplierRequest, Success>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "admin");

    /// <inheritdoc/>
    public override async Task HandleAsync(SetPreferredSupplierRequest request, CancellationToken ct)
    {
        var command = new SetPreferredSupplierCommand(request.VariantId, request.SupplierId);
        await bus.InvokeAsync<Success>(command, ct);
        await Send.NoContentAsync(ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/api/v1/admin/catalog/variants/{variantId}/suppliers/{supplierId}/preferred");
        Version(0);
    }
}

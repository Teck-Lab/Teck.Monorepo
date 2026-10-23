using Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Changes a variant-supplier cost price.</summary>
/// <param name="bus">The message bus.</param>
public sealed class UpdateSupplierCostEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<UpdateSupplierCostRequest, VariantSupplierDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "admin");

    /// <inheritdoc/>
    public override async Task HandleAsync(UpdateSupplierCostRequest request, CancellationToken ct)
    {
        var command = new UpdateSupplierCostCommand(
            request.VariantId, request.SupplierId, request.CostAmount, request.CostCurrency);
        var result = await bus.InvokeAsync<VariantSupplierDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/api/v1/admin/catalog/variants/{variantId}/suppliers/{supplierId}/cost");
        Version(0);
    }
}

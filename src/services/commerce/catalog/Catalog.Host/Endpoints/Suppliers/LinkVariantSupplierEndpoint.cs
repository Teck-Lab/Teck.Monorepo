using Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Links a supplier to a variant.</summary>
/// <param name="bus">The message bus.</param>
public sealed class LinkVariantSupplierEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<LinkVariantSupplierRequest, VariantSupplierDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(LinkVariantSupplierRequest request, CancellationToken ct)
    {
        var command = new LinkVariantSupplierCommand(
            request.VariantId, request.SupplierId, request.CostAmount, request.CostCurrency,
            request.SupplierSku, request.LeadTimeDays, request.MinOrderQuantity, request.IsPreferred);
        var result = await bus.InvokeAsync<VariantSupplierDto>(command, ct);
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/variants/{variantId}/suppliers");
        Version(0);
    }
}

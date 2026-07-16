using Catalog.Application.Suppliers.Features.GetSupplierPriceHistory.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Fetches the cost history for a variant-supplier link.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetSupplierPriceHistoryEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetSupplierPriceHistoryRequest, IReadOnlyList<SupplierPriceHistoryDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetSupplierPriceHistoryRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<SupplierPriceHistoryDto>>(
            new GetSupplierPriceHistoryQuery(request.VariantId, request.SupplierId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/variants/{variantId}/suppliers/{supplierId}/history");
        Version(0);
    }
}

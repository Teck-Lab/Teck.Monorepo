using Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Adds or updates a product's price within a price list.</summary>
/// <param name="bus">The message bus.</param>
public sealed class AddOrUpdatePriceEndpoint(IMessageBus bus) : AuthenticatedEndpoint<AddOrUpdatePriceRequest, PriceListDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(AddOrUpdatePriceRequest request, CancellationToken ct)
    {
        var command = new AddOrUpdatePriceCommand(request.Id, request.ProductId, request.Amount, request.Tiers);
        var result = await bus.InvokeAsync<PriceListDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/price-lists/{id}/prices/{productId}");
        Version(0);
    }
}

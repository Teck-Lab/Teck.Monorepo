using Pricing.Application.Pricing.Features.ActivatePriceList.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Activates a price list.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ActivatePriceListEndpoint(IMessageBus bus) : AuthenticatedEndpoint<ActivatePriceListRequest, PriceListDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(ActivatePriceListRequest request, CancellationToken ct)
    {
        var command = new ActivatePriceListCommand(request.Id);
        var result = await bus.InvokeAsync<PriceListDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/price-lists/{id}/activate");
        Version(0);
    }
}

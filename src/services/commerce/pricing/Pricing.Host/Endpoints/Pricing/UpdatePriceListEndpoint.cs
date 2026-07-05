using Pricing.Application.Pricing.Features.UpdatePriceList.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Updates a price list's details, scope, and validity.</summary>
/// <param name="bus">The message bus.</param>
public sealed class UpdatePriceListEndpoint(IMessageBus bus) : AuthenticatedEndpoint<UpdatePriceListRequest, PriceListDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(UpdatePriceListRequest request, CancellationToken ct)
    {
        var command = new UpdatePriceListCommand(
            request.Id, request.Name, request.Description, request.Currency, request.Country,
            request.CustomerGroupId, request.ChannelId, request.ValidFrom, request.ValidUntil);
        var result = await bus.InvokeAsync<PriceListDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/price-lists/{id}");
        Version(0);
    }
}

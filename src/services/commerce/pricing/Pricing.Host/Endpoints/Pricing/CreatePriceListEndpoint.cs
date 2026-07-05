using Pricing.Application.Pricing.Features.CreatePriceList.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Creates a new draft price list.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreatePriceListEndpoint(IMessageBus bus) : AuthenticatedEndpoint<CreatePriceListRequest, PriceListDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreatePriceListRequest request, CancellationToken ct)
    {
        var command = new CreatePriceListCommand(
            request.Name, request.Description, request.Currency, request.Country,
            request.CustomerGroupId, request.ChannelId, request.ValidFrom, request.ValidUntil);
        var result = await bus.InvokeAsync<PriceListDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/price-lists/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/price-lists");
        Version(0);
    }
}

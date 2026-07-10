using Pricing.Application.Pricing.Features.GetPriceList.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Retrieves a single price list by identifier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetPriceListEndpoint(IMessageBus bus) : AuthenticatedEndpoint<GetPriceListRequest, PriceListDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetPriceListRequest request, CancellationToken ct)
    {
        var query = new GetPriceListQuery(request.Id);
        var result = await bus.InvokeAsync<PriceListDto>(query, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/price-lists/{id}");
        Version(0);
    }
}

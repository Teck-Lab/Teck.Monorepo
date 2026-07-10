using FastEndpoints;
using Pricing.Application.Pricing.Features.ListPriceLists.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Lists all price lists for the tenant.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ListPriceListsEndpoint(IMessageBus bus) : AuthenticatedEndpoint<EmptyRequest, IReadOnlyList<PriceListDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<PriceListDto>>(new ListPriceListsQuery(), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/price-lists");
        Version(0);
    }
}

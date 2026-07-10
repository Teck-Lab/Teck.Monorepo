using Pricing.Application.Pricing.Features.ArchivePriceList.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Archives a price list.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ArchivePriceListEndpoint(IMessageBus bus) : AuthenticatedEndpoint<ArchivePriceListRequest, PriceListDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(ArchivePriceListRequest request, CancellationToken ct)
    {
        var command = new ArchivePriceListCommand(request.Id);
        var result = await bus.InvokeAsync<PriceListDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/price-lists/{id}/archive");
        Version(0);
    }
}

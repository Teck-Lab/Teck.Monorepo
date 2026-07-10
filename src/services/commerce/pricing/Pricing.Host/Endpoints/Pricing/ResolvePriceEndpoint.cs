using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Resolves the effective price for a product in a request context.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ResolvePriceEndpoint(IMessageBus bus) : AuthenticatedEndpoint<ResolvePriceRequest, ResolvedPriceDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(ResolvePriceRequest request, CancellationToken ct)
    {
        var query = new ResolvePriceQuery(
            request.ProductId, request.Currency, request.Quantity ?? 1,
            request.Country, request.CustomerGroupId, request.ChannelId, request.At);
        var result = await bus.InvokeAsync<ResolvedPriceDto>(query, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/prices/resolve");
        Version(0);
    }
}

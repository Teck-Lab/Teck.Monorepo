using FastEndpoints;
using Pricing.Application.Pricing.Features.ListExchangeRates.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Lists all exchange rates for the tenant.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetExchangeRatesEndpoint(IMessageBus bus) : AuthenticatedEndpoint<EmptyRequest, IReadOnlyList<ExchangeRateDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<ExchangeRateDto>>(new ListExchangeRatesQuery(), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/exchange-rates");
        Version(0);
    }
}

using Pricing.Application.Pricing.Features.SetExchangeRate.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Creates or updates the exchange rate for a currency pair.</summary>
/// <param name="bus">The message bus.</param>
public sealed class SetExchangeRateEndpoint(IMessageBus bus) : AuthenticatedEndpoint<SetExchangeRateRequest, ExchangeRateDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(SetExchangeRateRequest request, CancellationToken ct)
    {
        var command = new SetExchangeRateCommand(
            request.FromCurrency, request.ToCurrency, request.Rate, request.ValidFrom, request.ValidUntil);
        var result = await bus.InvokeAsync<ExchangeRateDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/exchange-rates");
        Version(0);
    }
}

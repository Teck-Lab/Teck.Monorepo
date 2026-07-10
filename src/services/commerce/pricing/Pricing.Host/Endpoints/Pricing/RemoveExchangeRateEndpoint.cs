using ErrorOr;
using Pricing.Application.Pricing.Features.RemoveExchangeRate.V1;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Removes the exchange rate for a currency pair.</summary>
/// <param name="bus">The message bus.</param>
public sealed class RemoveExchangeRateEndpoint(IMessageBus bus) : AuthenticatedEndpoint<RemoveExchangeRateRequest, Success>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(RemoveExchangeRateRequest request, CancellationToken ct)
    {
        var command = new RemoveExchangeRateCommand(request.FromCurrency, request.ToCurrency);
        await bus.InvokeAsync<Success>(command, ct);
        await Send.NoContentAsync(ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Delete("/exchange-rates/{fromCurrency}/{toCurrency}");
        Version(0);
    }
}

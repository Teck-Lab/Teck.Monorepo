using Billings.Application.Billing;
using Billings.Application.Billing.Payments;
using Microsoft.Extensions.Options;

namespace Billings.Host.Infrastructure;

/// <summary>
/// A stub <see cref="IPaymentProvider"/> that simulates capture outcomes based on
/// <see cref="PaymentProviderOptions.SimulateSuccess"/>. It never contacts a real payment gateway
/// and never handles real card data — it exists purely so the billing service can be exercised
/// end-to-end before a real provider integration is built.
/// </summary>
/// <param name="options">The payment provider options controlling the simulated outcome.</param>
public sealed class StubPaymentProvider(IOptions<PaymentProviderOptions> options) : IPaymentProvider
{
    /// <inheritdoc/>
    public Task<PaymentProviderResult> CaptureAsync(Guid orderId, decimal amount, string currency, CancellationToken ct)
    {
        PaymentProviderResult result = CreateResult(orderId, options.Value.StubOutcome, options.Value.StubDeclineCode, options.Value.SimulateSuccess);

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<PaymentProviderResult> AttemptAsync(PaymentProviderRequest request, CancellationToken ct) =>
        Task.FromResult(CreateResult(request.OrderId, options.Value.StubOutcome, options.Value.StubDeclineCode, options.Value.SimulateSuccess));

    private static PaymentProviderResult CreateResult(Guid orderId, string configuredOutcome, string configuredCode, bool simulateSuccess)
    {
        var outcome = string.IsNullOrWhiteSpace(configuredOutcome) ? (simulateSuccess ? "succeeded" : "failed") : configuredOutcome;
        var success = string.Equals(outcome, "succeeded", StringComparison.OrdinalIgnoreCase);
        return new PaymentProviderResult(success, success ? $"stub-{orderId:N}" : null, success ? null : "declined")
        {
            Outcome = outcome,
            ProviderCode = success ? null : configuredCode,
        };
    }
}

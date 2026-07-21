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
        PaymentProviderResult result = options.Value.SimulateSuccess
            ? new PaymentProviderResult(true, $"stub-{orderId:N}", null)
            : new PaymentProviderResult(false, null, "Payment declined by stub provider");

        return Task.FromResult(result);
    }
}

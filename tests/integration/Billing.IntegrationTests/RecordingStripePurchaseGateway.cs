using Billings.Application.Billing.Purchases;
using Billings.Domain.Entities;

namespace Billing.IntegrationTests;

/// <summary>Simulates Stripe only inside isolated Billing HTTP tests; records the server-priced intent.</summary>
public sealed class RecordingStripePurchaseGateway : IStripePurchaseGateway
{
    private readonly Dictionary<string, StripePurchaseIntent> created = new(StringComparer.Ordinal);
    private readonly HashSet<string> captured = new(StringComparer.Ordinal);

    public long? CreatedAmountMinor { get; private set; }

    public string? CreatedCurrency { get; private set; }

    public int CreateCount { get; private set; }

    public bool FailAfterProviderAcceptsOnce { get; set; }

    public void MarkCaptured(string intentId) => captured.Add(intentId);

    public Task<StripePurchaseIntent> CreateAsync(TenantPurchaseOrder order, CancellationToken cancellationToken)
    {
        CreateCount++;
        CreatedAmountMinor = order.AmountMinor;
        CreatedCurrency = order.Currency;
        var intent = new StripePurchaseIntent($"pi_{order.Id:N}", $"pi_{order.Id:N}_secret_isolated-test-only");
        created.TryAdd(intent.PaymentIntentId, intent);
        if (FailAfterProviderAcceptsOnce)
        {
            FailAfterProviderAcceptsOnce = false;
            throw new InvalidOperationException("Test provider accepted the intent but the response was lost.");
        }

        return Task.FromResult(intent);
    }

    public Task<StripePurchaseIntent> GetAsync(string intentId, CancellationToken cancellationToken) =>
        Task.FromResult(created[intentId]);

    public Task<bool> IsCapturedAsync(TenantPurchaseOrder order, CancellationToken cancellationToken) =>
        Task.FromResult(order.StripePaymentIntentId is not null && captured.Contains(order.StripePaymentIntentId));
}

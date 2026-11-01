using Billings.Domain.Entities;
using Billings.Host.Infrastructure;
using SharedKernel.Core.Pricing;
using Stripe;
using Xunit;

namespace Billing.IntegrationTests;

public sealed class StripePurchaseGatewayTests
{
    [Fact]
    public async Task Create_UsesImmutableServerPriceAndStableIdempotencyKey()
    {
        var order = CreateOrder();
        var stripe = new FakePaymentIntents { Result = IntentFor(order) };
        var gateway = new StripePurchaseGateway(stripe);

        var result = await gateway.CreateAsync(order, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(stripe.Result.Id, result.PaymentIntentId);
        Assert.Equal(order.AmountMinor, stripe.Created!.Amount);
        Assert.Equal(order.Currency.ToLowerInvariant(), stripe.Created.Currency);
        Assert.Equal(order.Id.ToString("D"), stripe.Created.Metadata["purchase_order_id"]);
        Assert.Equal(order.TenantId.ToString("D"), stripe.Created.Metadata["customer_tenant_id"]);
        Assert.Equal($"tenant-purchase-{order.Id:D}", stripe.CreateRequest!.IdempotencyKey);
    }

    [Theory]
    [InlineData("requires_action", 14999, 14999, "usd", true)]
    [InlineData("succeeded", 14999, 0, "usd", true)]
    [InlineData("succeeded", 14999, 14999, "eur", true)]
    [InlineData("succeeded", 14999, 14999, "usd", false)]
    public async Task VerifyCapture_RejectsUncapturedOrMismatchedProviderEvidence(
        string status, long amount, long received, string currency, bool validMetadata)
    {
        var order = CreateOrder();
        order.ReserveStripeIntentAttempt(DateTimeOffset.UtcNow);
        order.BindStripePaymentIntent("pi_test_purchase");
        var intent = IntentFor(order);
        intent.Status = status;
        intent.Amount = amount;
        intent.AmountReceived = received;
        intent.Currency = currency;
        if (!validMetadata)
        {
            intent.Metadata["purchase_order_id"] = Guid.NewGuid().ToString("D");
        }

        var gateway = new StripePurchaseGateway(new FakePaymentIntents { Result = intent });
        Assert.False(await gateway.IsCapturedAsync(order, CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task VerifyCapture_AcceptsOnlyRetrievedSucceededExactOrder()
    {
        var order = CreateOrder();
        order.ReserveStripeIntentAttempt(DateTimeOffset.UtcNow);
        order.BindStripePaymentIntent("pi_test_purchase");
        var gateway = new StripePurchaseGateway(new FakePaymentIntents { Result = IntentFor(order) });

        Assert.True(await gateway.IsCapturedAsync(order, CancellationToken.None).ConfigureAwait(false));
    }

    private static TenantPurchaseOrder CreateOrder() => TenantPurchaseOrder.Create(
        Guid.NewGuid(), $"stripe-{Guid.NewGuid():N}", TenantPlan.Business, "2026-10",
        TenantPlan.Business.DatabaseStrategy, "postgres", false, 0, new Dictionary<string, decimal>(),
        DateTimeOffset.UtcNow, "owner", "owner");

    private static PaymentIntent IntentFor(TenantPurchaseOrder order) => new()
    {
        Id = "pi_test_purchase",
        ClientSecret = "pi_test_purchase_secret_test-only",
        Status = "succeeded",
        Amount = order.AmountMinor,
        AmountReceived = order.AmountMinor,
        Currency = "usd",
        Metadata = new Dictionary<string, string>
        {
            ["purchase_order_id"] = order.Id.ToString("D"),
            ["customer_tenant_id"] = order.TenantId.ToString("D"),
        },
    };

    private sealed class FakePaymentIntents : PaymentIntentService
    {
        internal PaymentIntent Result { get; init; } = null!;

        internal PaymentIntentCreateOptions? Created { get; private set; }

        internal RequestOptions? CreateRequest { get; private set; }

        public override Task<PaymentIntent> CreateAsync(PaymentIntentCreateOptions options, RequestOptions requestOptions = null!, CancellationToken cancellationToken = default)
        {
            Created = options;
            CreateRequest = requestOptions;
            return Task.FromResult(Result);
        }

        public override Task<PaymentIntent> GetAsync(string id, PaymentIntentGetOptions options = null!, RequestOptions requestOptions = null!, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }
}

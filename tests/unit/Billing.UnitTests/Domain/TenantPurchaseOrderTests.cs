using Billings.Domain.Entities;
using SharedKernel.Core.Pricing;
using System.Globalization;
using Xunit;

namespace Billing.UnitTests.Domain;

public sealed class TenantPurchaseOrderTests
{
    [Fact]
    public void Create_CalculatesPriceFromPlanAndConfiguredAddonRates()
    {
        var order = TenantPurchaseOrder.Create(
            Guid.NewGuid(), "acme", TenantPlan.Business, "2026-10", DatabaseStrategy.Dedicated,
            "postgres", hasReadReplicas: true, replicaCount: 2,
            monthlyAddonRates: new Dictionary<string, decimal> { ["read-replica"] = 15m },
            createdAtUtc: DateTimeOffset.Parse("2026-10-25T10:00:00Z", CultureInfo.InvariantCulture),
            actorSubjectId: "buyer", beneficiarySubjectId: "buyer");

        Assert.Equal(17999, order.AmountMinor);
        Assert.Equal("USD", order.Currency);
        Assert.Equal("pending", order.Status);
        Assert.Equal(2, order.ReplicaCount);
        Assert.Equal(17999, order.AmountMinor);
        Assert.Equal(DatabaseStrategy.Dedicated.Name, order.DatabaseStrategy);
        Assert.Equal("postgres", order.DatabaseProvider);
    }

    [Fact]
    public void Create_RejectsUnknownPlanAndUnpricedOptions()
    {
        Assert.Throws<ArgumentException>(() => TenantPurchaseOrder.Create(
            Guid.NewGuid(), "acme", TenantPlan.None, "2026-10", DatabaseStrategy.Shared,
            "postgres", false, 0, new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer"));
        Assert.Throws<ArgumentException>(() => TenantPurchaseOrder.Create(
            Guid.NewGuid(), "acme", TenantPlan.Business, "2026-10", DatabaseStrategy.Dedicated,
            "postgres", true, 1, new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer"));
    }

    [Fact]
    public void ConfirmCapturedPayment_ActivatesExactlyOnceAndRejectsLateOrDuplicateConfirmation()
    {
        var order = CreateOrder();
        var subscriptionId = Guid.NewGuid();
        order.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow);

        Assert.Equal("active", order.Status);
        Assert.Equal(subscriptionId, order.SubscriptionId);
        Assert.Throws<InvalidOperationException>(() => order.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Cancel_RequiresConfirmedPaymentAndRecordsSuspensionTimeOnce()
    {
        var order = CreateOrder();
        var cancelledAt = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidOperationException>(() => order.Cancel(cancelledAt));
        Assert.Equal("pending", order.Status);

        order.ConfirmCapturedPayment(Guid.NewGuid(), cancelledAt.AddMinutes(-1));
        order.Cancel(cancelledAt);
        Assert.Equal("cancelled", order.Status);
        Assert.Equal(cancelledAt, order.CancelledAtUtc);
        Assert.Throws<InvalidOperationException>(() => order.Cancel(cancelledAt.AddDays(1)));
        Assert.Equal(cancelledAt, order.CancelledAtUtc);
        Assert.Throws<InvalidOperationException>(() => order.ConfirmCapturedPayment(Guid.NewGuid(), cancelledAt));
    }

    [Fact]
    public void Failed_renewal_cannot_suspend_before_expiry_and_records_one_expired_transition()
    {
        var order = CreateOrder();
        var confirmedAt = DateTimeOffset.Parse("2025-10-25T10:00:00Z", CultureInfo.InvariantCulture);
        Assert.Throws<InvalidOperationException>(() => order.SuspendForFailedRenewal(confirmedAt));
        order.ConfirmCapturedPayment(Guid.NewGuid(), confirmedAt);
        Assert.Throws<InvalidOperationException>(() => order.SuspendForFailedRenewal(confirmedAt.AddMonths(11)));
        Assert.Equal("active", order.Status);

        var expiry = confirmedAt.AddYears(1);
        order.SuspendForFailedRenewal(expiry);
        Assert.Equal("suspended", order.Status);
        Assert.Equal(expiry, order.CancelledAtUtc);
        Assert.Throws<InvalidOperationException>(() => order.SuspendForFailedRenewal(expiry.AddDays(1)));
        Assert.Equal(expiry, order.CancelledAtUtc);
    }

    [Fact]
    public void ConfirmExternalInvoice_RequiresAuditorAndAuditReference()
    {
        var order = CreateOrder();

        Assert.Throws<ArgumentException>(() => order.ConfirmExternalInvoice(Guid.NewGuid(), "", "sales-user", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => order.ConfirmExternalInvoice(Guid.NewGuid(), "INV-42", "", DateTimeOffset.UtcNow));
        order.ConfirmExternalInvoice(Guid.NewGuid(), "INV-42", "sales-user", DateTimeOffset.UtcNow);

        Assert.Equal("external_invoice", order.PaymentMethod);
        Assert.Equal("INV-42", order.ExternalInvoiceReference);
        Assert.Equal("sales-user", order.InvoiceVerifiedBy);
    }

    [Fact]
    public void BindStripePaymentIntent_RejectsReplacementAndLateBinding()
    {
        var order = CreateOrder();
        order.BindStripePaymentIntent("pi_first");
        order.BindStripePaymentIntent("pi_first");
        Assert.Equal("pi_first", order.StripePaymentIntentId);
        Assert.Throws<InvalidOperationException>(() => order.BindStripePaymentIntent("pi_another"));
        order.ConfirmCapturedPayment(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => order.BindStripePaymentIntent("pi_first"));
    }

    [Fact]
    public void PaidRenewal_UsesNewOrderAndStableSubscriptionWithoutShorteningExistingPeriod()
    {
        var initial = CreateOrder();
        var subscriptionId = Guid.NewGuid();
        var firstStart = DateTimeOffset.Parse("2025-10-25T10:00:00Z", CultureInfo.InvariantCulture);
        initial.ConfirmCapturedPayment(subscriptionId, firstStart);
        var next = TenantPurchaseOrder.Create(initial.TenantId, initial.TenantIdentifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), firstStart.AddMonths(11), "buyer", "buyer");

        Assert.NotEqual(initial.Id, next.Id);
        Assert.Equal("pending", next.Status);
        Assert.Equal(Guid.Empty, next.SubscriptionId);
        next.ConfirmRenewalPayment(subscriptionId, initial.ValidUntilUtc!.Value, firstStart.AddMonths(11));
        Assert.Equal(subscriptionId, next.SubscriptionId);
        Assert.Equal(initial.ValidUntilUtc.Value.AddYears(1), next.ValidUntilUtc);
        Assert.Throws<InvalidOperationException>(() => next.ConfirmRenewalPayment(subscriptionId,
            initial.ValidUntilUtc.Value, firstStart.AddMonths(11)));
    }

    [Fact]
    public void LatePaidRenewal_StartsFreshYearAfterExpiredPeriodWithoutUnpaidExtension()
    {
        var order = CreateOrder();
        var now = DateTimeOffset.UtcNow;
        order.ConfirmRenewalPayment(Guid.NewGuid(), now.AddDays(-10), now);
        Assert.Equal(now.AddYears(1), order.ValidUntilUtc);
    }

    private static TenantPurchaseOrder CreateOrder() => TenantPurchaseOrder.Create(
        Guid.NewGuid(), "acme", TenantPlan.Business, "2026-10", DatabaseStrategy.Dedicated,
        "postgres", false, 0, new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
}

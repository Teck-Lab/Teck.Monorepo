using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class PaymentLifecycleTests
{
    [Fact]
    public void Create_WhenAmountExceedsAuthorization_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Payment.Create("tenant", Guid.NewGuid(), Guid.Empty, new Money(11m, "USD"), new Money(10m, "USD"), "pm_token", "request-1", "correlation-1"));
    }

    [Fact]
    public void ApplyOutcome_WhenSucceeded_CapturesOnce()
    {
        var payment = Payment.Create("tenant", Guid.NewGuid(), Guid.Empty, new Money(10m, "USD"), new Money(10m, "USD"), "pm_token", "request-1", "correlation-1");
        var attempt = payment.BeginAttempt("request-1");

        payment.ApplyOutcome(attempt, PaymentAttemptStatus.Succeeded, "provider-reference", null, null, null, DateTimeOffset.UtcNow);
        payment.ApplyOutcome(attempt, PaymentAttemptStatus.Succeeded, "provider-reference", null, null, null, DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Single(payment.Attempts);
        Assert.Equal("provider-reference", payment.ProviderReference);
    }

    [Fact]
    public void Cancel_WhenRepeatedRequest_IsIdempotent()
    {
        var payment = Payment.Create("tenant", Guid.NewGuid(), Guid.Empty, new Money(10m, "USD"), new Money(10m, "USD"), "pm_token", "request-1", "correlation-1");

        payment.Cancel("cancel-1");
        payment.Cancel("cancel-1");

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("cancel-1", payment.CancellationRequestId);
    }
}

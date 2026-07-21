using Billings.Domain.DomainEvents;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using Xunit;

namespace Billing.UnitTests.Domain;

public sealed class PaymentTests
{
    private static readonly Money DefaultAmount = new(25.00m, "USD");

    [Fact]
    public void Create_SetsPendingStatusAndFields()
    {
        var tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var payment = Payment.Create(tenantId, orderId, customerId, DefaultAmount);

        Assert.Equal(tenantId, payment.TenantId);
        Assert.Equal(orderId, payment.OrderId);
        Assert.Equal(customerId, payment.CustomerId);
        Assert.Equal(DefaultAmount, payment.Amount);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ProviderReference);
        Assert.Empty(payment.DomainEvents);
    }

    [Fact]
    public void Create_RejectsEmptyOrderId() =>
        Assert.Throws<ArgumentException>(() => Payment.Create("tenant-1", Guid.Empty, Guid.NewGuid(), DefaultAmount));

    [Fact]
    public void Create_RejectsEmptyCustomerId() =>
        Assert.Throws<ArgumentException>(() => Payment.Create("tenant-1", Guid.NewGuid(), Guid.Empty, DefaultAmount));

    [Fact]
    public void MarkCaptured_FromPending_SetsCapturedAndRaisesEvent()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), DefaultAmount);

        payment.MarkCaptured("tok_abc123");

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("tok_abc123", payment.ProviderReference);

        PaymentCaptured evt = Assert.Single(payment.DomainEvents.OfType<PaymentCaptured>());
        Assert.Equal(payment.Id, evt.PaymentId);
        Assert.Equal(payment.OrderId, evt.OrderId);
        Assert.Equal(payment.TenantId, evt.TenantId);
        Assert.Equal(DefaultAmount.Amount, evt.Amount);
        Assert.Equal(DefaultAmount.Currency, evt.Currency);
        Assert.Equal("tok_abc123", evt.ProviderReference);
    }

    [Fact]
    public void MarkFailed_FromPending_SetsFailedAndRaisesEvent()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), DefaultAmount);

        payment.MarkFailed("card_declined");

        Assert.Equal(PaymentStatus.Failed, payment.Status);

        PaymentFailed evt = Assert.Single(payment.DomainEvents.OfType<PaymentFailed>());
        Assert.Equal(payment.Id, evt.PaymentId);
        Assert.Equal(payment.OrderId, evt.OrderId);
        Assert.Equal(payment.TenantId, evt.TenantId);
        Assert.Equal("card_declined", evt.Reason);
    }

    [Fact]
    public void MarkCaptured_AlreadyCaptured_Throws()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), DefaultAmount);
        payment.MarkCaptured("tok_abc123");

        Assert.Throws<InvalidOperationException>(() => payment.MarkCaptured("tok_xyz789"));
    }

    [Fact]
    public void MarkFailed_AlreadyFailed_Throws()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), DefaultAmount);
        payment.MarkFailed("card_declined");

        Assert.Throws<InvalidOperationException>(() => payment.MarkFailed("card_declined_again"));
    }

    [Fact]
    public void MarkCaptured_AfterFailed_Throws()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), DefaultAmount);
        payment.MarkFailed("card_declined");

        Assert.Throws<InvalidOperationException>(() => payment.MarkCaptured("tok_abc123"));
    }

    [Fact]
    public void MarkCaptured_BlankProviderReference_Throws()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), DefaultAmount);

        Assert.Throws<ArgumentException>(() => payment.MarkCaptured(" "));
    }

    [Fact]
    public void MarkFailed_BlankReason_Throws()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), DefaultAmount);

        Assert.Throws<ArgumentException>(() => payment.MarkFailed(" "));
    }
}

using Ardalis.Specification;
using Billings.Application.Billing.Payments;
using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class CapturePaymentHandlerTests
{
    private static (IGenericWriteRepository<Payment, Guid> Payments, IGenericWriteRepository<Invoice, Guid> Invoices) Repos(Payment? existing = null)
    {
        var payments = Substitute.For<IGenericWriteRepository<Payment, Guid>>();
        payments.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(existing));
        var invoices = Substitute.For<IGenericWriteRepository<Invoice, Guid>>();
        return (payments, invoices);
    }

    private static ITenantInfo Tenant()
    {
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        return tenant;
    }

    [Fact]
    public async Task Handle_ProviderSucceeds_CapturesCreatesInvoiceAndPublishesCaptured()
    {
        var (payments, invoices) = Repos();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = Substitute.For<IPaymentProvider>();
        provider.CaptureAsync(Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PaymentProviderResult(true, "tok_abc123", null)));
        var tenant = Tenant();
        var bus = Substitute.For<IMessageBus>();
        var command = new CapturePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 42.50m, "USD");

        var result = await CapturePaymentHandler.Handle(command, payments, invoices, unitOfWork, provider, tenant, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Captured", result.Value.Status);
        Assert.Equal(command.OrderId, result.Value.OrderId);
        Assert.Equal(42.50m, result.Value.Amount);
        Assert.Equal("tok_abc123", result.Value.ProviderReference);

        await payments.Received(1).AddAsync(Arg.Is<Payment>(p => p.Status == PaymentStatus.Captured), Arg.Any<CancellationToken>());
        await invoices.Received(1).AddAsync(Arg.Any<Invoice>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await bus.Received(1).PublishAsync(Arg.Is<PaymentCapturedIntegrationEvent>(evt =>
            evt.OrderId == command.OrderId
            && evt.TenantId == "tenant-1"
            && evt.Amount == 42.50m
            && evt.Currency == "USD"));
        await bus.DidNotReceive().PublishAsync(Arg.Any<PaymentFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_ProviderDeclines_FailsPaymentNoInvoiceAndPublishesFailed()
    {
        var (payments, invoices) = Repos();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = Substitute.For<IPaymentProvider>();
        provider.CaptureAsync(Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PaymentProviderResult(false, null, "declined")));
        var tenant = Tenant();
        var bus = Substitute.For<IMessageBus>();
        var command = new CapturePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 15m, "USD");

        var result = await CapturePaymentHandler.Handle(command, payments, invoices, unitOfWork, provider, tenant, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Failed", result.Value.Status);
        Assert.Null(result.Value.ProviderReference);

        await payments.Received(1).AddAsync(Arg.Is<Payment>(p => p.Status == PaymentStatus.Failed), Arg.Any<CancellationToken>());
        await invoices.DidNotReceive().AddAsync(Arg.Any<Invoice>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await bus.Received(1).PublishAsync(Arg.Is<PaymentFailedIntegrationEvent>(evt =>
            evt.OrderId == command.OrderId
            && evt.Reason == "declined"));
        await bus.DidNotReceive().PublishAsync(Arg.Any<PaymentCapturedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_OrderAlreadyHasPayment_ReturnsExistingWithoutCallingProviderOrPublishing()
    {
        var orderId = Guid.NewGuid();
        var existing = Payment.Create("tenant-1", orderId, Guid.NewGuid(), new Money(10m, "USD"));
        existing.MarkCaptured("tok_existing");
        var (payments, invoices) = Repos(existing);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = Substitute.For<IPaymentProvider>();
        var tenant = Tenant();
        var bus = Substitute.For<IMessageBus>();
        var command = new CapturePaymentCommand(orderId, Guid.NewGuid(), 999m, "USD");

        var result = await CapturePaymentHandler.Handle(command, payments, invoices, unitOfWork, provider, tenant, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(existing.Id, result.Value.Id);
        Assert.Equal("Captured", result.Value.Status);
        Assert.Equal("tok_existing", result.Value.ProviderReference);

        await provider.DidNotReceive().CaptureAsync(Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await payments.DidNotReceive().AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await invoices.DidNotReceive().AddAsync(Arg.Any<Invoice>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<PaymentCapturedIntegrationEvent>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<PaymentFailedIntegrationEvent>());
    }
}

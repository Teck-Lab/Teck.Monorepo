using Ardalis.Specification;
using Billings.Application.Billing.Payments;
using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;
using Billings.Application.Billing.Payments.Responses;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using SharedKernel.Core.Database;
using Wolverine;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class CapturePaymentHandlerTests
{
    private static (IGenericWriteRepository<Payment, Guid> Payments, IGenericWriteRepository<Invoice, Guid> Invoices, IGenericWriteRepository<PaymentAttempt, Guid> Attempts) Repos(Payment? existing = null, PaymentAttempt? existingAttempt = null)
    {
        var payments = Substitute.For<IGenericWriteRepository<Payment, Guid>>();
        payments.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(existing));
        var invoices = Substitute.For<IGenericWriteRepository<Invoice, Guid>>();
        var attempts = Substitute.For<IGenericWriteRepository<PaymentAttempt, Guid>>();
        attempts.FirstOrDefaultAsync(Arg.Any<ISpecification<PaymentAttempt>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(existingAttempt));
        return (payments, invoices, attempts);
    }

    private static ITenantInfo Tenant()
    {
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        return tenant;
    }

    private static void ConfigureOutcome(IMessageBus bus, PaymentDto payment) =>
        bus.InvokeAsync<ErrorOr<PaymentDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<PaymentDto>>(payment));

    private static LifecycleCapturePaymentCommand ValidLifecycleCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 42.50m, "USD")
        {
            AuthorizedAmount = 50m,
            PaymentMethodToken = "pm_test_token",
            RequestId = "request-1",
            SourceCorrelationId = "correlation-1",
        };

    [Fact]
    public async Task Handle_ProviderSucceeds_PersistsPendingAttemptBeforeInvokingTheOutcomeHandler()
    {
        var (payments, _, attempts) = Repos();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = Substitute.For<IPaymentProvider>();
        provider.AttemptAsync(Arg.Any<PaymentProviderRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PaymentProviderResult(true, "tok_abc123", null)));
        var tenant = Tenant();
        var bus = Substitute.For<IMessageBus>();
        var command = new CapturePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 42.50m, "USD");
        ConfigureOutcome(bus, new PaymentDto(Guid.NewGuid(), command.OrderId, command.CustomerId, command.Amount, command.Currency, "Captured", "tok_abc123"));

        var result = await CapturePaymentHandler.Handle(command, payments, attempts, unitOfWork, provider, tenant, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Captured", result.Value.Status);
        Assert.Equal(command.OrderId, result.Value.OrderId);
        Assert.Equal(42.50m, result.Value.Amount);

        await payments.Received(1).AddAsync(Arg.Is<Payment>(p => p.RequestId == $"legacy-{command.OrderId:N}"), Arg.Any<CancellationToken>());
        await attempts.Received(1).AddAsync(Arg.Is<PaymentAttempt>(attempt => attempt.RequestId == $"legacy-{command.OrderId:N}"), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await provider.Received(1).AttemptAsync(Arg.Is<PaymentProviderRequest>(request => request.RequestId == $"legacy-{command.OrderId:N}"), Arg.Any<CancellationToken>());
        await bus.Received(1).InvokeAsync<ErrorOr<PaymentDto>>(Arg.Is<ProcessPaymentOutcomeCommand>(outcome => outcome.IsLegacy && outcome.RequestId == $"legacy-{command.OrderId:N}"), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            payments.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
            attempts.AddAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>());
            unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            provider.AttemptAsync(Arg.Any<PaymentProviderRequest>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_ProviderDeclines_InvokesTheSameLegacyOutcomeHandler()
    {
        var (payments, _, attempts) = Repos();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = Substitute.For<IPaymentProvider>();
        provider.AttemptAsync(Arg.Any<PaymentProviderRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PaymentProviderResult(false, null, "declined")));
        var tenant = Tenant();
        var bus = Substitute.For<IMessageBus>();
        var command = new CapturePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 15m, "USD");
        ConfigureOutcome(bus, new PaymentDto(Guid.NewGuid(), command.OrderId, command.CustomerId, command.Amount, command.Currency, "Failed", null));

        var result = await CapturePaymentHandler.Handle(command, payments, attempts, unitOfWork, provider, tenant, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Failed", result.Value.Status);

        await payments.Received(1).AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await attempts.Received(1).AddAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).InvokeAsync<ErrorOr<PaymentDto>>(Arg.Is<ProcessPaymentOutcomeCommand>(outcome => outcome.IsLegacy && outcome.Outcome == "failed"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderAlreadyHasPayment_ReturnsExistingWithoutCallingProviderOrPublishing()
    {
        var orderId = Guid.NewGuid();
        var existing = Payment.Create("tenant-1", orderId, Guid.NewGuid(), new Money(10m, "USD"));
        var existingAttempt = existing.BeginAttempt(existing.RequestId);
        existing.ApplyOutcome(existingAttempt, PaymentAttemptStatus.Succeeded, "tok_existing", null, null, null, DateTimeOffset.UtcNow);
        var (payments, _, attempts) = Repos(existing, existingAttempt);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = Substitute.For<IPaymentProvider>();
        var tenant = Tenant();
        var bus = Substitute.For<IMessageBus>();
        var command = new CapturePaymentCommand(orderId, Guid.NewGuid(), 999m, "USD");

        var result = await CapturePaymentHandler.Handle(command, payments, attempts, unitOfWork, provider, tenant, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(existing.Id, result.Value.Id);
        Assert.Equal("Captured", result.Value.Status);
        Assert.Equal("tok_existing", result.Value.ProviderReference);

        await provider.DidNotReceive().AttemptAsync(Arg.Any<PaymentProviderRequest>(), Arg.Any<CancellationToken>());
        await payments.DidNotReceive().AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await attempts.DidNotReceive().AddAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().InvokeAsync<ErrorOr<PaymentDto>>(Arg.Any<ProcessPaymentOutcomeCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("missing-authorization")]
    [InlineData("non-positive-authorization")]
    [InlineData("blank-token")]
    [InlineData("over-limit-token")]
    [InlineData("blank-request")]
    [InlineData("over-limit-request")]
    [InlineData("invalid-currency")]
    [InlineData("over-scale-amount")]
    [InlineData("over-scale-authorization")]
    [InlineData("over-ceiling")]
    public async Task Handle_InvalidV2Authority_ReturnsValidationWithoutProviderCallOrPersistence(string invalidInput)
    {
        var (payments, _, attempts) = Repos();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = Substitute.For<IPaymentProvider>();
        var bus = Substitute.For<IMessageBus>();
        var command = InvalidLifecycleCommand(invalidInput);

        var result = await CapturePaymentHandler.Handle(command, payments, attempts, unitOfWork, provider, Tenant(), bus, CancellationToken.None);

        Assert.True(result.IsError);
        await payments.DidNotReceive().FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>());
        await payments.DidNotReceive().AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await attempts.DidNotReceive().AddAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await provider.DidNotReceive().AttemptAsync(Arg.Any<PaymentProviderRequest>(), Arg.Any<CancellationToken>());
        await provider.DidNotReceive().CaptureAsync(Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidV2Authority_UsesStableIdempotentProviderRequest()
    {
        var (payments, _, attempts) = Repos();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = Substitute.For<IPaymentProvider>();
        provider.AttemptAsync(Arg.Any<PaymentProviderRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PaymentProviderResult(true, "provider-reference", null)));
        var command = ValidLifecycleCommand();

        var bus = Substitute.For<IMessageBus>();
        ConfigureOutcome(bus, new PaymentDto(Guid.NewGuid(), command.OrderId, command.CustomerId, command.Amount, command.Currency, "Captured", "provider-reference"));
        var result = await CapturePaymentHandler.Handle(command, payments, attempts, unitOfWork, provider, Tenant(), bus, CancellationToken.None);

        Assert.False(result.IsError);
        await provider.Received(1).AttemptAsync(
            Arg.Is<PaymentProviderRequest>(request => request.OrderId == command.OrderId
                && request.Amount == command.Amount
                && request.Currency == command.Currency
                && request.PaymentMethodToken == command.PaymentMethodToken
                && request.RequestId == command.RequestId),
            Arg.Any<CancellationToken>());
        await provider.DidNotReceive().CaptureAsync(Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await payments.Received(1).AddAsync(Arg.Is<Payment>(payment => payment.AuthorizedAmount.Amount == command.AuthorizedAmount && payment.AuthorizedAmount.Currency == command.Currency), Arg.Any<CancellationToken>());
        await attempts.Received(1).AddAsync(Arg.Is<PaymentAttempt>(attempt => attempt.RequestId == command.RequestId), Arg.Any<CancellationToken>());
        await bus.Received(1).InvokeAsync<ErrorOr<PaymentDto>>(Arg.Is<ProcessPaymentOutcomeCommand>(outcome => !outcome.IsLegacy && outcome.RequestId == command.RequestId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_V2ExistingTerminalPaymentWithDifferentRequestId_ReturnsExistingWithoutProviderCall()
    {
        var original = ValidLifecycleCommand();
        var existing = Payment.Create("tenant-1", original.OrderId, original.CustomerId, new Money(original.Amount, original.Currency), new Money(original.AuthorizedAmount, original.Currency), original.PaymentMethodToken, original.RequestId, original.SourceCorrelationId);
        var existingAttempt = existing.BeginAttempt(original.RequestId);
        existing.ApplyOutcome(existingAttempt, PaymentAttemptStatus.Succeeded, "provider-reference", null, null, null, DateTimeOffset.UtcNow);
        var (payments, _, attempts) = Repos(existing, existingAttempt);
        var replay = original with { RequestId = "request-different" };
        var provider = Substitute.For<IPaymentProvider>();
        var bus = Substitute.For<IMessageBus>();

        var result = await CapturePaymentHandler.Handle(replay, payments, attempts, Substitute.For<IUnitOfWork>(), provider, Tenant(), bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(existing.Id, result.Value.Id);
        await attempts.DidNotReceive().FirstOrDefaultAsync(Arg.Any<ISpecification<PaymentAttempt>>(), Arg.Any<CancellationToken>());
        await attempts.DidNotReceive().AddAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>());
        await provider.DidNotReceive().AttemptAsync(Arg.Any<PaymentProviderRequest>(), Arg.Any<CancellationToken>());
        await bus.DidNotReceive().InvokeAsync<ErrorOr<PaymentDto>>(Arg.Any<ProcessPaymentOutcomeCommand>(), Arg.Any<CancellationToken>());
    }

    private static LifecycleCapturePaymentCommand InvalidLifecycleCommand(string invalidInput)
    {
        var command = ValidLifecycleCommand();
        return invalidInput switch
        {
            "missing-authorization" => command with { AuthorizedAmount = 0m },
            "non-positive-authorization" => command with { AuthorizedAmount = -1m },
            "blank-token" => command with { PaymentMethodToken = " " },
            "over-limit-token" => command with { PaymentMethodToken = new string('t', 257) },
            "blank-request" => command with { RequestId = " " },
            "over-limit-request" => command with { RequestId = new string('r', 129) },
            "invalid-currency" => command with { Currency = "usd" },
            "over-scale-amount" => command with { Amount = 42.501m },
            "over-scale-authorization" => command with { AuthorizedAmount = 50.001m },
            "over-ceiling" => command with { Amount = 50.01m },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidInput), invalidInput, null),
        };
    }
}

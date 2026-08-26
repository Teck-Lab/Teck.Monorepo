using Ardalis.Specification;
using Billings.Application.Billing;
using Billings.Application.Billing.Payments;
using Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;
using Billings.Application.Billing.Payments.Features.RetryPayment.V1;
using Billings.Application.Billing.Payments.Responses;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using ErrorOr;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class PaymentRetryOrdinalTests
{
    [Fact]
    public async Task ProcessOutcome_ZeroRetryBound_DoesNotScheduleARetry()
    {
        var fixture = PendingOutcome(attemptNumber: 1);
        var bus = await ProcessTransientOutcomeAsync(fixture, maxRetries: 0);

        await bus.DidNotReceive().SendAsync(Arg.Any<RetryPaymentCommand>());
    }

    [Fact]
    public async Task ProcessOutcome_OneRetryBound_SchedulesRetryOne()
    {
        var fixture = PendingOutcome(attemptNumber: 1);
        var bus = await ProcessTransientOutcomeAsync(fixture, maxRetries: 1);

        await bus.Received(1).SendAsync(Arg.Is<RetryPaymentCommand>(command => command.RequestId == $"{fixture.Payment.RequestId}-retry-1"));
        await bus.DidNotReceive().PublishAsync(Arg.Any<PaymentFailedV2IntegrationEvent>());
    }

    [Fact]
    public async Task ProcessOutcome_MaxRetryBound_SchedulesThePersistedMaximumOrdinal()
    {
        var fixture = PendingOutcome(attemptNumber: 2);
        var bus = await ProcessTransientOutcomeAsync(fixture, maxRetries: 2);

        await bus.Received(1).SendAsync(Arg.Is<RetryPaymentCommand>(command => command.RequestId == $"{fixture.Payment.RequestId}-retry-2"));
        await bus.DidNotReceive().PublishAsync(Arg.Any<PaymentFailedV2IntegrationEvent>());
    }

    [Fact]
    public async Task ProcessOutcome_ExhaustedRetryBound_PublishesOneSafeOutcomeAndNeverSchedulesAgain()
    {
        var fixture = PendingOutcome(attemptNumber: 3);
        var bus = await ProcessTransientOutcomeAsync(fixture, maxRetries: 2, replay: true);

        await bus.DidNotReceive().SendAsync(Arg.Any<RetryPaymentCommand>());
        await bus.Received(1).PublishAsync(Arg.Any<PaymentFailedV2IntegrationEvent>());
    }

    [Fact]
    public async Task RetryHandler_FreshScopes_DeriveMonotonicOrdinalsAndUniqueProviderKeysFromPersistedAttempts()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), new Money(42.50m, "USD"), new Money(50m, "USD"), "pm_original", "request-1", "correlation-1");
        var initialAttempts = Attempts(payment, maximumOrdinal: 2);
        var first = await ExecuteRetryAsync(payment, initialAttempts, $"{payment.RequestId}-retry-2");

        var reloadedAttempts = Attempts(payment, maximumOrdinal: 3);
        var second = await ExecuteRetryAsync(payment, reloadedAttempts, $"{payment.RequestId}-retry-3");

        Assert.Equal(3, first.CreatedAttempt.AttemptNumber);
        Assert.Equal(4, second.CreatedAttempt.AttemptNumber);
        Assert.NotEqual(first.ProviderRequest.RequestId, second.ProviderRequest.RequestId);
        Assert.Equal($"{payment.RequestId}-retry-2", first.ProviderRequest.RequestId);
        Assert.Equal($"{payment.RequestId}-retry-3", second.ProviderRequest.RequestId);
    }

    private static async Task<IMessageBus> ProcessTransientOutcomeAsync(OutcomeFixture fixture, int maxRetries, bool replay = false)
    {
        var payments = Substitute.For<IGenericWriteRepository<Payment, Guid>>();
        payments.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payment?>(fixture.Payment));
        var attempts = Substitute.For<IGenericWriteRepository<PaymentAttempt, Guid>>();
        attempts.FirstOrDefaultAsync(Arg.Any<ISpecification<PaymentAttempt>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PaymentAttempt?>(fixture.CurrentAttempt));
        attempts.ListAsync(Arg.Any<ISpecification<PaymentAttempt>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fixture.PersistedAttempts));
        var invoices = Substitute.For<IGenericWriteRepository<Invoice, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        var options = Options.Create(new PaymentProviderOptions { MaxTransientRetries = maxRetries });
        var resolverOptions = Substitute.For<IOptionsMonitor<PaymentProviderOptions>>();
        resolverOptions.CurrentValue.Returns(new PaymentProviderOptions
        {
            DeclineMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["temporary"] = "transient" },
        });
        var command = new ProcessPaymentOutcomeCommand(fixture.Payment.OrderId, fixture.CurrentAttempt.RequestId, "failed", null, "temporary");

        var first = await ProcessPaymentOutcomeHandler.Handle(command, payments, attempts, invoices, unitOfWork, new DeclineCategoryResolver(resolverOptions), options, bus, CancellationToken.None).ConfigureAwait(false);
        Assert.False(first.IsError);
        if (replay)
        {
            var second = await ProcessPaymentOutcomeHandler.Handle(command, payments, attempts, invoices, unitOfWork, new DeclineCategoryResolver(resolverOptions), options, bus, CancellationToken.None).ConfigureAwait(false);
            Assert.True(second.IsError);
        }

        return bus;
    }

    private static async Task<RetryExecution> ExecuteRetryAsync(Payment payment, IReadOnlyList<PaymentAttempt> persistedAttempts, string requestId)
    {
        var payments = Substitute.For<IGenericWriteRepository<Payment, Guid>>();
        payments.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payment?>(payment));
        var attempts = Substitute.For<IGenericWriteRepository<PaymentAttempt, Guid>>();
        attempts.FirstOrDefaultAsync(Arg.Any<ISpecification<PaymentAttempt>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PaymentAttempt?>(null));
        attempts.ListAsync(Arg.Any<ISpecification<PaymentAttempt>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(persistedAttempts));
        PaymentAttempt? createdAttempt = null;
        attempts.AddAsync(Arg.Do<PaymentAttempt>(attempt => createdAttempt = attempt), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var provider = Substitute.For<IPaymentProvider>();
        PaymentProviderRequest? providerRequest = null;
        provider.AttemptAsync(Arg.Do<PaymentProviderRequest>(request => providerRequest = request), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PaymentProviderResult(true, "provider-reference", null)));
        var bus = Substitute.For<IMessageBus>();
        bus.InvokeAsync<ErrorOr<PaymentDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<PaymentDto>>(new PaymentDto(payment.Id, payment.OrderId, payment.CustomerId, payment.Amount.Amount, payment.Amount.Currency, "Captured", "provider-reference")));
        var command = new RetryPaymentCommand(payment.OrderId, payment.AuthorizedAmount.Amount, payment.AuthorizedAmount.Currency, "pm_replacement", requestId, payment.SourceCorrelationId);

        var result = await RetryPaymentHandler.Handle(command, payments, attempts, Substitute.For<IUnitOfWork>(), provider, bus, CancellationToken.None).ConfigureAwait(false);

        Assert.False(result.IsError);
        Assert.NotNull(createdAttempt);
        Assert.NotNull(providerRequest);
        await provider.Received(1).AttemptAsync(Arg.Any<PaymentProviderRequest>(), Arg.Any<CancellationToken>()).ConfigureAwait(false);
        return new RetryExecution(createdAttempt!, providerRequest!);
    }

    private static OutcomeFixture PendingOutcome(int attemptNumber)
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), new Money(42.50m, "USD"), new Money(50m, "USD"), "pm_original", "request-1", "correlation-1");
        var persistedAttempts = Attempts(payment, attemptNumber);
        return new OutcomeFixture(payment, persistedAttempts[^1], persistedAttempts);
    }

    private static PaymentAttempt[] Attempts(Payment payment, int maximumOrdinal) =>
        Enumerable.Range(1, maximumOrdinal)
            .Select(ordinal => PaymentAttempt.Create(payment.TenantId, payment.Id, ordinal == 1 ? payment.RequestId : $"{payment.RequestId}-retry-{ordinal - 1}", ordinal, payment.Amount))
            .ToArray();

    private sealed record OutcomeFixture(Payment Payment, PaymentAttempt CurrentAttempt, IReadOnlyList<PaymentAttempt> PersistedAttempts);

    private sealed record RetryExecution(PaymentAttempt CreatedAttempt, PaymentProviderRequest ProviderRequest);
}

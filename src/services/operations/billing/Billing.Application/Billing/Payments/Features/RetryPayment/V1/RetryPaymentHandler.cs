using Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;
using Billings.Application.Billing.Payments.Mapping;
using Billings.Application.Billing.Payments.ReadModels;
using Billings.Application.Billing.Payments.Responses;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using ErrorOr;
using SharedKernel.Core.Database;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Payments.Features.RetryPayment.V1;

/// <summary>Retries a payment only when its amount and currency remain within the original ceiling.</summary>
[NonTransactional]
public static class RetryPaymentHandler
{
    /// <summary>Handles an idempotent retry request.</summary>
    /// <param name="command">The retry command.</param>
    /// <param name="payments">The tracked payment repository.</param>
    /// <param name="attempts">The attempt repository.</param>
    /// <param name="unitOfWork">The commit boundary.</param>
    /// <param name="provider">The configured provider adapter.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated payment representation.</returns>
    public static async Task<ErrorOr<PaymentDto>> Handle(
        RetryPaymentCommand command,
        IGenericWriteRepository<Payment, Guid> payments,
        IGenericWriteRepository<PaymentAttempt, Guid> attempts,
        IUnitOfWork unitOfWork,
        IPaymentProvider provider,
        IMessageBus bus,
        CancellationToken ct)
    {
        var payment = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(command.OrderId), enableTracking: true, ct).ConfigureAwait(false);
        if (payment is null || payment.Status == PaymentStatus.Captured || payment.CancellationRequestId is not null)
        {
            throw new InvalidOperationException("Payment cannot be retried.");
        }

        if (payment.Amount.Amount > command.AuthorizedAmount || !string.Equals(payment.Amount.Currency, command.Currency, StringComparison.Ordinal) || !string.Equals(payment.AuthorizedAmount.Currency, command.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Retry exceeds the authorized payment ceiling.");
        }

        var duplicate = await attempts.FirstOrDefaultAsync(new PaymentAttemptByRequestIdSpec(command.RequestId), ct).ConfigureAwait(false);
        if (duplicate is not null && duplicate.Status != PaymentAttemptStatus.Pending && duplicate.Status != PaymentAttemptStatus.Processing)
        {
            return payment.ToDto();
        }

        var attempt = duplicate;
        if (attempt is null)
        {
            var persistedAttempts = await attempts.ListAsync(new PaymentAttemptsByPaymentSpec(payment.Id), enableTracking: true, ct).ConfigureAwait(false);
            var nextAttemptNumber = persistedAttempts.Count == 0 ? 1 : persistedAttempts.Max(existingAttempt => existingAttempt.AttemptNumber) + 1;
            payment.ReplacePaymentMethod(command.PaymentMethodToken);
            attempt = PaymentAttempt.Create(payment.TenantId, payment.Id, command.RequestId, nextAttemptNumber, payment.Amount);
            await attempts.AddAsync(attempt, ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var result = await provider.AttemptAsync(new PaymentProviderRequest(payment.OrderId, payment.Amount.Amount, payment.Amount.Currency, payment.PaymentMethodToken, attempt.RequestId), ct).ConfigureAwait(false);
        return await bus.InvokeAsync<ErrorOr<PaymentDto>>(new ProcessPaymentOutcomeCommand(payment.OrderId, attempt.RequestId, result.Outcome, result.ProviderReference, result.ProviderCode ?? result.FailureReason), ct).ConfigureAwait(false);
    }
}

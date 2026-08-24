using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Application.Billing.Payments.Features.RetryPayment.V1;
using Billings.Application.Billing.Payments.Mapping;
using Billings.Application.Billing.Payments.ReadModels;
using Billings.Application.Billing.Payments.Responses;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using ErrorOr;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;

/// <summary>Updates the aggregate for immediate and delayed provider outcomes.</summary>
public static class ProcessPaymentOutcomeHandler
{
    /// <summary>Handles a delayed or redelivered provider outcome.</summary>
    /// <param name="command">The provider outcome command.</param>
    /// <param name="payments">The tracked payment repository.</param>
    /// <param name="attempts">The tracked attempt repository.</param>
    /// <param name="invoices">The tracked invoice repository.</param>
    /// <param name="unitOfWork">The commit boundary.</param>
    /// <param name="declineResolver">The safe decline resolver.</param>
    /// <param name="options">The retry policy options.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The reconciled payment so an immediate provider outcome is visible before the caller returns.</returns>
    [Transactional]
    public static async Task<ErrorOr<PaymentDto>> Handle(
        ProcessPaymentOutcomeCommand command,
        IGenericWriteRepository<Payment, Guid> payments,
        IGenericWriteRepository<PaymentAttempt, Guid> attempts,
        IGenericWriteRepository<Invoice, Guid> invoices,
        IUnitOfWork unitOfWork,
        DeclineCategoryResolver declineResolver,
        IOptions<PaymentProviderOptions> options,
        IMessageBus bus,
        CancellationToken ct)
    {
        var payment = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(command.OrderId), enableTracking: true, ct).ConfigureAwait(false);
        var attempt = await attempts.FirstOrDefaultAsync(new PaymentAttemptByRequestIdSpec(command.RequestId), enableTracking: true, ct).ConfigureAwait(false);
        if (payment is null || attempt is null || attempt.PaymentId != payment.Id || attempt.Status != PaymentAttemptStatus.Pending && attempt.Status != PaymentAttemptStatus.Processing)
        {
            return Error.NotFound("payment.outcome.not_pending", "The payment outcome does not target a pending payment attempt.");
        }

        var result = new PaymentProviderResult(string.Equals(command.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase), command.ProviderReference, command.ProviderCode)
        {
            Outcome = command.Outcome,
            ProviderCode = command.ProviderCode,
        };
        var outcome = PaymentOutcomeSupport.Normalize(result, declineResolver);
        payment.ApplyOutcome(attempt, outcome.AttemptStatus, command.ProviderReference, command.ProviderCode, outcome.DeclineCategory, outcome.MappingAuditHash, DateTimeOffset.UtcNow);
        if (payment.Status == PaymentStatus.Captured)
        {
            var amount = payment.Amount;
            await invoices.AddAsync(Invoice.Create(payment.TenantId, payment.OrderId, amount, [new InvoiceLineInput(payment.OrderId, $"Order {payment.OrderId}", 1, amount)], DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        var shouldRetry = false;
        if (outcome.DeclineCategory == DeclineCategory.Transient)
        {
            var persistedAttempts = await attempts.ListAsync(new PaymentAttemptsByPaymentSpec(payment.Id), ct).ConfigureAwait(false);
            var retryOrdinal = persistedAttempts.Max(persistedAttempt => persistedAttempt.AttemptNumber);
            shouldRetry = retryOrdinal <= options.Value.MaxTransientRetries;
            if (shouldRetry)
            {
                await bus.SendAsync(new RetryPaymentCommand(payment.OrderId, payment.AuthorizedAmount.Amount, payment.AuthorizedAmount.Currency, payment.PaymentMethodToken, $"{payment.RequestId}-retry-{retryOrdinal}", payment.SourceCorrelationId)).ConfigureAwait(false);
            }
        }

        if (command.IsLegacy)
        {
            await CapturePaymentHandler.PublishLegacyOutcomeAsync(payment, bus).ConfigureAwait(false);
        }
        else if (!shouldRetry)
        {
            await CapturePaymentHandler.PublishOutcomeAsync(payment, attempt, outcome, bus).ConfigureAwait(false);
        }

        return payment.ToDto();
    }
}

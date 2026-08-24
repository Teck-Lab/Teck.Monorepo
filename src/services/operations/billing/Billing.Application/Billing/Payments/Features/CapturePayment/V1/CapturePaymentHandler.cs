using Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;
using Billings.Application.Billing.Payments.Mapping;
using Billings.Application.Billing.Payments.ReadModels;
using Billings.Application.Billing.Payments.Responses;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Payments.Features.CapturePayment.V1;

/// <summary>Creates an idempotent payment and applies its immediate provider result.</summary>
[NonTransactional]
public static class CapturePaymentHandler
{
    /// <summary>Handles the isolated pre-flag V1 order traffic retained during rollout.</summary>
    /// <param name="command">The legacy capture command.</param>
    /// <param name="payments">The payment repository.</param>
    /// <param name="attempts">The payment-attempt repository.</param>
    /// <param name="unitOfWork">The commit boundary.</param>
    /// <param name="provider">The configured provider.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The resulting payment representation.</returns>
    public static async Task<ErrorOr<PaymentDto>> Handle(
        CapturePaymentCommand command,
        IGenericWriteRepository<Payment, Guid> payments,
        IGenericWriteRepository<PaymentAttempt, Guid> attempts,
        IUnitOfWork unitOfWork,
        IPaymentProvider provider,
        ITenantInfo tenant,
        IMessageBus bus,
        CancellationToken ct)
    {
        var existing = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(command.OrderId), ct).ConfigureAwait(false);
        Payment payment;
        PaymentAttempt attempt;
        if (existing is null)
        {
            var money = new Money(command.Amount, command.Currency);
            payment = Payment.Create(tenant.Id ?? string.Empty, command.OrderId, command.CustomerId, money);
            attempt = payment.BeginAttempt(payment.RequestId);
            await payments.AddAsync(payment, ct).ConfigureAwait(false);
            await attempts.AddAsync(attempt, ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            payment = existing;
            var existingAttempt = await attempts.FirstOrDefaultAsync(new PaymentAttemptByRequestIdSpec(payment.RequestId), ct).ConfigureAwait(false);
            if (existingAttempt is null || existingAttempt.Status != PaymentAttemptStatus.Pending && existingAttempt.Status != PaymentAttemptStatus.Processing)
            {
                return payment.ToDto();
            }

            attempt = existingAttempt;
        }

        var result = await provider.AttemptAsync(new PaymentProviderRequest(payment.OrderId, payment.Amount.Amount, payment.Amount.Currency, payment.PaymentMethodToken, attempt.RequestId), ct).ConfigureAwait(false);
        return await bus.InvokeAsync<ErrorOr<PaymentDto>>(new ProcessPaymentOutcomeCommand(payment.OrderId, attempt.RequestId, result.Outcome, result.ProviderReference, result.ProviderCode ?? result.FailureReason, IsLegacy: true), ct).ConfigureAwait(false);
    }

    /// <summary>Handles a checkout lifecycle payment request.</summary>
    /// <param name="command">The lifecycle-derived capture command.</param>
    /// <param name="payments">The tracked payment repository.</param>
    /// <param name="attempts">The tracked payment-attempt repository.</param>
    /// <param name="unitOfWork">The payment commit boundary.</param>
    /// <param name="provider">The configured provider adapter.</param>
    /// <param name="tenant">The current message tenant.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The captured payment representation.</returns>
    public static async Task<ErrorOr<PaymentDto>> Handle(
        LifecycleCapturePaymentCommand command,
        IGenericWriteRepository<Payment, Guid> payments,
        IGenericWriteRepository<PaymentAttempt, Guid> attempts,
        IUnitOfWork unitOfWork,
        IPaymentProvider provider,
        ITenantInfo tenant,
        IMessageBus bus,
        CancellationToken ct)
    {
        var validationError = command.Validate();
        if (validationError is not null)
        {
            return validationError.Value;
        }

        var existing = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(command.OrderId), ct).ConfigureAwait(false);
        Payment payment;
        PaymentAttempt attempt;
        if (existing is null)
        {
            var amount = new Money(command.Amount, command.Currency);
            var authorizedAmount = new Money(command.AuthorizedAmount, command.Currency);
            payment = Payment.Create(tenant.Id ?? string.Empty, command.OrderId, command.CustomerId, amount, authorizedAmount, command.PaymentMethodToken, command.RequestId, command.SourceCorrelationId);
            attempt = payment.BeginAttempt(command.RequestId);
            await payments.AddAsync(payment, ct).ConfigureAwait(false);
            await attempts.AddAsync(attempt, ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            payment = existing;
            if (payment.RequestId != command.RequestId)
            {
                return payment.ToDto();
            }

            var existingAttempt = await attempts.FirstOrDefaultAsync(new PaymentAttemptByRequestIdSpec(payment.RequestId), ct).ConfigureAwait(false);
            if (existingAttempt is null || existingAttempt.Status != PaymentAttemptStatus.Pending && existingAttempt.Status != PaymentAttemptStatus.Processing)
            {
                return payment.ToDto();
            }

            attempt = existingAttempt;
        }

        var result = await provider.AttemptAsync(new PaymentProviderRequest(payment.OrderId, payment.Amount.Amount, payment.Amount.Currency, payment.PaymentMethodToken, attempt.RequestId), ct).ConfigureAwait(false);
        return await bus.InvokeAsync<ErrorOr<PaymentDto>>(new ProcessPaymentOutcomeCommand(payment.OrderId, attempt.RequestId, result.Outcome, result.ProviderReference, result.ProviderCode ?? result.FailureReason), ct).ConfigureAwait(false);
    }

    internal static async Task PublishLegacyOutcomeAsync(Payment payment, IMessageBus bus)
    {
        if (payment.Status == PaymentStatus.Captured)
        {
            await bus.PublishAsync(new PaymentCapturedIntegrationEvent { PaymentId = payment.Id, OrderId = payment.OrderId, TenantId = payment.TenantId, Amount = payment.Amount.Amount, Currency = payment.Amount.Currency }).ConfigureAwait(false);
        }
        else
        {
            await bus.PublishAsync(new PaymentFailedIntegrationEvent { PaymentId = payment.Id, OrderId = payment.OrderId, TenantId = payment.TenantId, Amount = payment.Amount.Amount, Currency = payment.Amount.Currency, Reason = payment.DeclineCategory?.Name ?? "declined" }).ConfigureAwait(false);
        }
    }

    internal static async Task PublishOutcomeAsync(Payment payment, PaymentAttempt attempt, PaymentOutcome outcome, IMessageBus bus)
    {
        if (payment.Status == PaymentStatus.Captured)
        {
            await bus.PublishAsync(new PaymentCapturedV2IntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                TenantId = payment.TenantId,
                Amount = payment.Amount.Amount,
                AuthorizedAmount = payment.AuthorizedAmount.Amount,
                Currency = payment.Amount.Currency,
                RequestId = attempt.RequestId,
                SourceCorrelationId = payment.SourceCorrelationId,
                CapturedAt = DateTimeOffset.UtcNow,
            }).ConfigureAwait(false);
        }
        else if (outcome.DeclineCategory is not null)
        {
            await bus.PublishAsync(new PaymentFailedV2IntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                TenantId = payment.TenantId,
                Amount = payment.Amount.Amount,
                AuthorizedAmount = payment.AuthorizedAmount.Amount,
                Currency = payment.Amount.Currency,
                DeclineCategory = outcome.DeclineCategory.Name,
                ActionText = PaymentOutcomeSupport.ActionText(outcome.DeclineCategory),
                RequestId = attempt.RequestId,
                SourceCorrelationId = payment.SourceCorrelationId,
                FailedAt = DateTimeOffset.UtcNow,
            }).ConfigureAwait(false);
        }
    }
}

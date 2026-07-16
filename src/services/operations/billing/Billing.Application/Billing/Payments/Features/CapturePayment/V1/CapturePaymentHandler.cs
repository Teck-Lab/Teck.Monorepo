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

namespace Billings.Application.Billing.Payments.Features.CapturePayment.V1;

/// <summary>Handles <see cref="CapturePaymentCommand"/>.</summary>
public static class CapturePaymentHandler
{
    /// <summary>
    /// Captures payment for an order via the configured <see cref="IPaymentProvider"/>, records the
    /// outcome, issues an invoice on success, commits, then publishes the corresponding integration
    /// event. Idempotent: replaying the same order returns the payment already on file instead of
    /// charging it again.
    /// </summary>
    /// <param name="command">The command describing the payment to capture.</param>
    /// <param name="payments">The write repository for payments.</param>
    /// <param name="invoices">The write repository for invoices.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="provider">The payment provider used to capture the funds.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="bus">The message bus used to publish the resulting integration event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The captured or already-processed payment as a <see cref="PaymentDto"/>.</returns>
    public static async Task<ErrorOr<PaymentDto>> Handle(
        CapturePaymentCommand command,
        IGenericWriteRepository<Payment, Guid> payments,
        IGenericWriteRepository<Invoice, Guid> invoices,
        IUnitOfWork unitOfWork,
        IPaymentProvider provider,
        ITenantInfo tenant,
        IMessageBus bus,
        CancellationToken ct)
    {
        var existing = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(command.OrderId), ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // Already processed — never re-charge an order that already has a payment on file.
            return existing.ToDto();
        }

        var money = new Money(command.Amount, command.Currency);
        var payment = Payment.Create(tenant.Id ?? string.Empty, command.OrderId, command.CustomerId, money);
        var result = await provider.CaptureAsync(command.OrderId, money, ct).ConfigureAwait(false);

        if (result.Success)
        {
            payment.MarkCaptured(result.ProviderReference!);

            var invoiceLines = new[]
            {
                new InvoiceLineInput(command.OrderId, $"Order {command.OrderId}", 1, money),
            };
            var invoice = Invoice.Create(tenant.Id ?? string.Empty, command.OrderId, money, invoiceLines, DateTimeOffset.UtcNow);
            await invoices.AddAsync(invoice, ct).ConfigureAwait(false);
        }
        else
        {
            payment.MarkFailed(result.FailureReason ?? "declined");
        }

        await payments.AddAsync(payment, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        if (result.Success)
        {
            await bus.PublishAsync(new PaymentCapturedIntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                TenantId = payment.TenantId,
                Amount = command.Amount,
                Currency = command.Currency,
            }).ConfigureAwait(false);
        }
        else
        {
            await bus.PublishAsync(new PaymentFailedIntegrationEvent
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                TenantId = payment.TenantId,
                Amount = command.Amount,
                Currency = command.Currency,
                Reason = result.FailureReason ?? "declined",
            }).ConfigureAwait(false);
        }

        return payment.ToDto();
    }
}

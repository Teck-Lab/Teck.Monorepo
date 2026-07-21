using Billings.Application.Billing.Payments.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Payments.Features.CapturePayment.V1;

/// <summary>Captures payment for an order. Idempotent — replaying the same order returns the existing payment.</summary>
/// <param name="OrderId">The identifier of the order being paid for.</param>
/// <param name="CustomerId">The identifier of the customer making the payment.</param>
/// <param name="Amount">The amount to capture.</param>
/// <param name="Currency">The ISO currency code of the amount to capture.</param>
public sealed record CapturePaymentCommand(Guid OrderId, Guid CustomerId, decimal Amount, string Currency) : ICommand<ErrorOr<PaymentDto>>;

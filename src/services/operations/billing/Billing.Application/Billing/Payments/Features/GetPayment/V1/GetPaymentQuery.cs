using Billings.Application.Billing.Payments.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Payments.Features.GetPayment.V1;

/// <summary>Fetches a payment by id.</summary>
/// <param name="PaymentId">The identifier of the payment to fetch.</param>
public sealed record GetPaymentQuery(Guid PaymentId) : IQuery<ErrorOr<PaymentDto>>;

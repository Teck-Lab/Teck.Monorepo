using Ardalis.Specification;
using Billings.Domain.Entities;

namespace Billings.Application.Billing.Payments.ReadModels;

/// <summary>Selects a single payment by id.</summary>
public sealed class PaymentByIdSpec : Specification<Payment>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="paymentId">The identifier of the payment to select.</param>
    public PaymentByIdSpec(Guid paymentId) => Query.Where(payment => payment.Id == paymentId);
}

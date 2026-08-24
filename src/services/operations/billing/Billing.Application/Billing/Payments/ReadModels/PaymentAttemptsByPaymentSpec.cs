using Ardalis.Specification;
using Billings.Domain.Entities;

namespace Billings.Application.Billing.Payments.ReadModels;

/// <summary>Selects the persisted tenant-scoped attempts belonging to one payment.</summary>
public sealed class PaymentAttemptsByPaymentSpec : Specification<PaymentAttempt>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="paymentId">The payment whose attempts are selected.</param>
    public PaymentAttemptsByPaymentSpec(Guid paymentId) => Query.Where(attempt => attempt.PaymentId == paymentId);
}

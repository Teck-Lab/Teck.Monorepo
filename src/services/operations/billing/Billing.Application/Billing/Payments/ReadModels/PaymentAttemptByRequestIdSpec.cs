using Ardalis.Specification;
using Billings.Domain.Entities;

namespace Billings.Application.Billing.Payments.ReadModels;

/// <summary>Selects the payment attempt for a tenant-scoped idempotency key.</summary>
public sealed class PaymentAttemptByRequestIdSpec : Specification<PaymentAttempt>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="requestId">The stable request identifier.</param>
    public PaymentAttemptByRequestIdSpec(string requestId) => Query.Where(attempt => attempt.RequestId == requestId);
}

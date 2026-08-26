using Ardalis.Specification;
using Billings.Domain.Entities;

namespace Billings.Application.Billing.Payments.ReadModels;

/// <summary>Selects a single payment by id for the authorized tenant.</summary>
public sealed class PaymentByIdSpec : Specification<Payment>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="paymentId">The identifier of the payment to select.</param>
    /// <param name="tenantId">The tenant authorized to read the payment.</param>
    public PaymentByIdSpec(Guid paymentId, string tenantId) =>
        Query.Where(payment => payment.Id == paymentId && payment.TenantId == tenantId);
}

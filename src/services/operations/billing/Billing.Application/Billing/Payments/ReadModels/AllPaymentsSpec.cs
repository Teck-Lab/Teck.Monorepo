using Ardalis.Specification;
using Billings.Domain.Entities;

namespace Billings.Application.Billing.Payments.ReadModels;

/// <summary>Selects all payments for the authorized tenant, most recently created first.</summary>
public sealed class AllPaymentsSpec : Specification<Payment>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="tenantId">The tenant authorized to list payments.</param>
    public AllPaymentsSpec(string tenantId) =>
        Query.Where(payment => payment.TenantId == tenantId)
            .OrderByDescending(payment => payment.CreatedAt);
}

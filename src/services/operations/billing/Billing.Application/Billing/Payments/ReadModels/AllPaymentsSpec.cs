using Ardalis.Specification;
using Billings.Domain.Entities;

namespace Billings.Application.Billing.Payments.ReadModels;

/// <summary>Selects all payments, most recently created first.</summary>
public sealed class AllPaymentsSpec : Specification<Payment>
{
    /// <summary>Initializes the spec.</summary>
    public AllPaymentsSpec() => Query.OrderByDescending(payment => payment.CreatedAt);
}

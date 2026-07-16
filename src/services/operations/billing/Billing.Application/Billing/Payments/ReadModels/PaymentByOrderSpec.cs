using Ardalis.Specification;
using Billings.Domain.Entities;

namespace Billings.Application.Billing.Payments.ReadModels;

/// <summary>
/// Selects the payment already recorded for an order, if any. Used to make payment capture
/// idempotent — an order must never be charged twice.
/// </summary>
public sealed class PaymentByOrderSpec : Specification<Payment>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="orderId">The identifier of the order whose payment is selected.</param>
    public PaymentByOrderSpec(Guid orderId) => Query.Where(payment => payment.OrderId == orderId);
}

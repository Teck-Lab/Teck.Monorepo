using SharedKernel.Core.Events;

namespace Billings.Domain.DomainEvents;

/// <summary>Raised when a payment attempt has failed.</summary>
public sealed class PaymentFailed : DomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentFailed"/> class.
    /// </summary>
    /// <param name="paymentId">The identifier of the failed payment.</param>
    /// <param name="orderId">The identifier of the order the payment belongs to.</param>
    /// <param name="tenantId">The identifier of the tenant that owns the payment.</param>
    /// <param name="reason">The reason the payment failed.</param>
    public PaymentFailed(Guid paymentId, Guid orderId, string tenantId, string reason)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        TenantId = tenantId;
        Reason = reason;
    }

    /// <summary>Gets the identifier of the failed payment.</summary>
    public Guid PaymentId { get; }

    /// <summary>Gets the identifier of the order the payment belongs to.</summary>
    public Guid OrderId { get; }

    /// <summary>Gets the identifier of the tenant that owns the payment.</summary>
    public string TenantId { get; }

    /// <summary>Gets the reason the payment failed.</summary>
    public string Reason { get; }
}

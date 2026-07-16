using SharedKernel.Core.Events;

namespace Billings.Domain.DomainEvents;

/// <summary>Raised when a payment has been captured.</summary>
public sealed class PaymentCaptured : DomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentCaptured"/> class.
    /// </summary>
    /// <param name="paymentId">The identifier of the captured payment.</param>
    /// <param name="orderId">The identifier of the order the payment belongs to.</param>
    /// <param name="tenantId">The identifier of the tenant that owns the payment.</param>
    /// <param name="amount">The captured amount.</param>
    /// <param name="currency">The ISO currency code of the captured amount.</param>
    /// <param name="providerReference">The tokenized reference returned by the payment provider.</param>
    public PaymentCaptured(Guid paymentId, Guid orderId, string tenantId, decimal amount, string currency, string providerReference)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        TenantId = tenantId;
        Amount = amount;
        Currency = currency;
        ProviderReference = providerReference;
    }

    /// <summary>Gets the identifier of the captured payment.</summary>
    public Guid PaymentId { get; }

    /// <summary>Gets the identifier of the order the payment belongs to.</summary>
    public Guid OrderId { get; }

    /// <summary>Gets the identifier of the tenant that owns the payment.</summary>
    public string TenantId { get; }

    /// <summary>Gets the captured amount.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the ISO currency code of the captured amount.</summary>
    public string Currency { get; }

    /// <summary>Gets the tokenized reference returned by the payment provider.</summary>
    public string ProviderReference { get; }
}

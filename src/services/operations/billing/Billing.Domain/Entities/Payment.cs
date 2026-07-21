using Billings.Domain.DomainEvents;
using Billings.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Billings.Domain.Entities;

/// <summary>
/// Represents a payment aggregate root for a customer order.
/// </summary>
public sealed class Payment : BaseEntity, IAggregateRoot, ITenantScoped
{
    private Payment()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the identifier of the order this payment is for.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Gets the identifier of the customer making the payment.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Gets the payment amount.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>Gets the current lifecycle status of the payment.</summary>
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    /// <summary>
    /// Gets the tokenized reference returned by the payment provider, if any.
    /// This must never contain raw card data.
    /// </summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Creates a new pending payment.</summary>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="orderId">The identifier of the order this payment is for.</param>
    /// <param name="customerId">The identifier of the customer making the payment.</param>
    /// <param name="amount">The payment amount.</param>
    /// <returns>The newly created pending payment.</returns>
    public static Payment Create(string tenantId, Guid orderId, Guid customerId, Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("CustomerId is required.", nameof(customerId));
        }

        return new Payment
        {
            TenantId = tenantId,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Status = PaymentStatus.Pending,
        };
    }

    /// <summary>Marks the payment as captured and raises <see cref="PaymentCaptured"/>.</summary>
    /// <param name="providerReference">The tokenized reference returned by the payment provider.</param>
    public void MarkCaptured(string providerReference)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new ArgumentException("ProviderReference is required.", nameof(providerReference));
        }

        EnsurePending();

        ProviderReference = providerReference;
        Status = PaymentStatus.Captured;

        AddDomainEvent(new PaymentCaptured(Id, OrderId, TenantId, Amount.Amount, Amount.Currency, providerReference));
    }

    /// <summary>Marks the payment as failed and raises <see cref="PaymentFailed"/>.</summary>
    /// <param name="reason">The reason the payment failed.</param>
    public void MarkFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        EnsurePending();

        Status = PaymentStatus.Failed;

        AddDomainEvent(new PaymentFailed(Id, OrderId, TenantId, reason));
    }

    private void EnsurePending()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Payment is '{Status.Name}' and cannot transition from a non-pending state.");
        }
    }
}

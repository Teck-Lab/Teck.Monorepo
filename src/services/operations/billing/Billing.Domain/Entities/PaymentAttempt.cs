using Billings.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Billings.Domain.Entities;

/// <summary>Records one idempotent, tokenized payment-provider attempt.</summary>
public sealed class PaymentAttempt : BaseEntity, ITenantScoped
{
    private PaymentAttempt()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the payment aggregate identifier.</summary>
    public Guid PaymentId { get; private set; }

    /// <summary>Gets the idempotency key supplied to the provider.</summary>
    public string RequestId { get; private set; } = string.Empty;

    /// <summary>Gets the one-based attempt number for the payment.</summary>
    public int AttemptNumber { get; private set; }

    /// <summary>Gets the amount sent to the provider.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>Gets the provider attempt status.</summary>
    public PaymentAttemptStatus Status { get; private set; } = PaymentAttemptStatus.Pending;

    /// <summary>Gets the provider reference, when the provider exposes one.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Gets the billing-private provider response code; it is never emitted in an event.</summary>
    public string? ProviderCode { get; private set; }

    /// <summary>Gets the safe decline category, when the attempt did not succeed.</summary>
    public DeclineCategory? DeclineCategory { get; private set; }

    /// <summary>Gets when a final outcome was applied.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Creates a pending attempt.</summary>
    /// <param name="tenantId">The tenant that owns the attempt.</param>
    /// <param name="paymentId">The payment aggregate identifier.</param>
    /// <param name="requestId">The stable idempotency key.</param>
    /// <param name="attemptNumber">The one-based attempt ordinal.</param>
    /// <param name="amount">The platform-resolved amount.</param>
    /// <returns>The newly created pending attempt.</returns>
    public static PaymentAttempt Create(string tenantId, Guid paymentId, string requestId, int attemptNumber, Money amount) => new()
    {
        TenantId = tenantId,
        PaymentId = paymentId,
        RequestId = string.IsNullOrWhiteSpace(requestId) ? throw new ArgumentException("RequestId is required.", nameof(requestId)) : requestId,
        AttemptNumber = attemptNumber,
        Amount = amount ?? throw new ArgumentNullException(nameof(amount)),
    };

    /// <summary>Applies a provider result exactly once.</summary>
    /// <param name="status">The normalized provider status.</param>
    /// <param name="providerReference">The tokenized provider reference.</param>
    /// <param name="providerCode">The billing-private provider code.</param>
    /// <param name="declineCategory">The shopper-safe decline category.</param>
    /// <param name="completedAt">The time the result was observed.</param>
    public void Complete(PaymentAttemptStatus status, string? providerReference, string? providerCode, DeclineCategory? declineCategory, DateTimeOffset completedAt)
    {
        if (Status != PaymentAttemptStatus.Pending && Status != PaymentAttemptStatus.Processing)
        {
            return;
        }

        Status = status;
        ProviderReference = providerReference;
        ProviderCode = providerCode;
        DeclineCategory = declineCategory;
        CompletedAt = completedAt;
    }
}

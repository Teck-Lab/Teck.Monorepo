using Billings.Domain.DomainEvents;
using Billings.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Billings.Domain.Entities;

/// <summary>Represents the provider-agnostic payment aggregate for one order.</summary>
public sealed class Payment : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<PaymentAttempt> _attempts = [];

    private Payment()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the order this payment belongs to.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Gets the optional legacy customer correlation.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Gets the platform-resolved amount that may be charged.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>Gets the shopper-authorized ceiling.</summary>
    public Money AuthorizedAmount { get; private set; } = null!;

    /// <summary>Gets the current payment lifecycle status.</summary>
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    /// <summary>Gets the opaque payment method reference; never raw card data.</summary>
    public string PaymentMethodToken { get; private set; } = string.Empty;

    /// <summary>Gets the stable checkout request identifier.</summary>
    public string RequestId { get; private set; } = string.Empty;

    /// <summary>Gets the source correlation identifier.</summary>
    public string SourceCorrelationId { get; private set; } = string.Empty;

    /// <summary>Gets the tokenized provider reference, if capture succeeded.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Gets the last shopper-safe decline category.</summary>
    public DeclineCategory? DeclineCategory { get; private set; }

    /// <summary>Gets the SHA-256 audit hash of the decline mapping applied to the latest outcome.</summary>
    public string? DeclineMappingAuditHash { get; private set; }

    /// <summary>Gets when the decline mapping audit was updated.</summary>
    public DateTimeOffset? DeclineMappingAuditedAt { get; private set; }

    /// <summary>Gets the idempotency key for the last applied cancellation.</summary>
    public string? CancellationRequestId { get; private set; }

    /// <summary>Gets the immutable attempt collection.</summary>
    public IReadOnlyList<PaymentAttempt> Attempts => _attempts;

    /// <summary>Creates a payment only when the platform amount is within the authorized ceiling.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="customerId">The optional legacy customer correlation.</param>
    /// <param name="amount">The platform-resolved amount.</param>
    /// <param name="authorizedAmount">The shopper-authorized ceiling.</param>
    /// <param name="paymentMethodToken">The opaque payment method token.</param>
    /// <param name="requestId">The stable provider request identifier.</param>
    /// <param name="sourceCorrelationId">The checkout correlation identifier.</param>
    /// <returns>The new pending payment.</returns>
    public static Payment Create(string tenantId, Guid orderId, Guid customerId, Money amount, Money authorizedAmount, string paymentMethodToken, string requestId, string sourceCorrelationId)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        }

        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(authorizedAmount);
        ValidateCeiling(amount, authorizedAmount);

        return new Payment
        {
            TenantId = tenantId,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            AuthorizedAmount = authorizedAmount,
            PaymentMethodToken = string.IsNullOrWhiteSpace(paymentMethodToken) ? throw new ArgumentException("PaymentMethodToken is required.", nameof(paymentMethodToken)) : paymentMethodToken,
            RequestId = string.IsNullOrWhiteSpace(requestId) ? throw new ArgumentException("RequestId is required.", nameof(requestId)) : requestId,
            SourceCorrelationId = sourceCorrelationId ?? string.Empty,
        };
    }

    /// <summary>Compatibility overload for previously persisted V1 lifecycle messages.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="customerId">The legacy customer identifier.</param>
    /// <param name="amount">The legacy amount.</param>
    /// <returns>A pending legacy-compatible payment.</returns>
    public static Payment Create(string tenantId, Guid orderId, Guid customerId, Money amount)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("CustomerId is required.", nameof(customerId));
        }

        return Create(tenantId, orderId, customerId, amount, amount, "legacy-token", $"legacy-{orderId:N}", string.Empty);
    }

    /// <summary>Records an idempotent provider attempt.</summary>
    /// <param name="requestId">The stable provider request identifier.</param>
    /// <returns>The existing or newly created attempt.</returns>
    public PaymentAttempt BeginAttempt(string requestId)
    {
        var existing = _attempts.SingleOrDefault(attempt => attempt.RequestId == requestId);
        if (existing is not null)
        {
            return existing;
        }

        var attempt = PaymentAttempt.Create(TenantId, Id, requestId, _attempts.Count + 1, Amount);
        _attempts.Add(attempt);
        return attempt;
    }

    /// <summary>Applies a normalized provider outcome to this payment.</summary>
    /// <param name="attempt">The attempt receiving the outcome.</param>
    /// <param name="attemptStatus">The normalized provider status.</param>
    /// <param name="providerReference">The tokenized provider reference.</param>
    /// <param name="providerCode">The billing-private provider code.</param>
    /// <param name="declineCategory">The shopper-safe decline category.</param>
    /// <param name="mappingAuditHash">The safe mapping audit hash.</param>
    /// <param name="observedAt">The time the result was observed.</param>
    public void ApplyOutcome(PaymentAttempt attempt, PaymentAttemptStatus attemptStatus, string? providerReference, string? providerCode, DeclineCategory? declineCategory, string? mappingAuditHash, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (Status == PaymentStatus.Captured || CancellationRequestId is not null)
        {
            return;
        }

        attempt.Complete(attemptStatus, providerReference, providerCode, declineCategory, observedAt);
        DeclineCategory = declineCategory;
        DeclineMappingAuditHash = mappingAuditHash;
        DeclineMappingAuditedAt = mappingAuditHash is null ? null : observedAt;

        if (attemptStatus == PaymentAttemptStatus.Succeeded)
        {
            ProviderReference = providerReference;
            Status = PaymentStatus.Captured;
            AddDomainEvent(new PaymentCaptured(Id, OrderId, TenantId, Amount.Amount, Amount.Currency, providerReference ?? string.Empty));
        }
        else if (attemptStatus == PaymentAttemptStatus.Processing)
        {
            Status = PaymentStatus.Pending;
        }
        else
        {
            Status = PaymentStatus.Failed;
            AddDomainEvent(new PaymentFailed(Id, OrderId, TenantId, declineCategory?.Name ?? "generic-decline"));
        }
    }

    /// <summary>Cancels the payment idempotently.</summary>
    /// <param name="requestId">The stable cancellation request key.</param>
    public void Cancel(string requestId)
    {
        if (CancellationRequestId == requestId || Status == PaymentStatus.Captured)
        {
            return;
        }

        CancellationRequestId = requestId;
        Status = PaymentStatus.Failed;
    }

    /// <summary>Changes the opaque token used by a shopper-initiated retry.</summary>
    /// <param name="paymentMethodToken">The replacement opaque payment token.</param>
    public void ReplacePaymentMethod(string paymentMethodToken) =>
        PaymentMethodToken = string.IsNullOrWhiteSpace(paymentMethodToken) ? throw new ArgumentException("PaymentMethodToken is required.", nameof(paymentMethodToken)) : paymentMethodToken;

    /// <summary>Compatibility transition for legacy callers.</summary>
    /// <param name="providerReference">The tokenized provider reference.</param>
    public void MarkCaptured(string providerReference)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new ArgumentException("ProviderReference is required.", nameof(providerReference));
        }

        EnsurePendingForLegacy();
        ApplyOutcome(BeginAttempt($"legacy-{Id:N}"), PaymentAttemptStatus.Succeeded, providerReference, null, null, null, DateTimeOffset.UtcNow);
    }

    /// <summary>Compatibility transition for legacy callers.</summary>
    /// <param name="reason">The safe failure reason.</param>
    public void MarkFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        EnsurePendingForLegacy();
        BeginAttempt($"legacy-{Id:N}").Complete(PaymentAttemptStatus.Failed, null, reason, DeclineCategory.GenericDecline, DateTimeOffset.UtcNow);
        DeclineCategory = DeclineCategory.GenericDecline;
        Status = PaymentStatus.Failed;
        AddDomainEvent(new PaymentFailed(Id, OrderId, TenantId, reason));
    }

    private static void ValidateCeiling(Money amount, Money authorizedAmount)
    {
        if (!string.Equals(amount.Currency, authorizedAmount.Currency, StringComparison.Ordinal) || amount.Amount > authorizedAmount.Amount)
        {
            throw new InvalidOperationException("The payment amount must not exceed the authorized amount and must use the authorized currency.");
        }
    }

    private void EnsurePendingForLegacy()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Payment is '{Status.Name}' and cannot transition from a non-pending state.");
        }
    }
}

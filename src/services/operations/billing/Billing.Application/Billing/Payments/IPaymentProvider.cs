using Billings.Domain.ValueObjects;

namespace Billings.Application.Billing.Payments;

/// <summary>
/// Abstracts the external payment gateway used to capture funds for an order. Implementations
/// live in the Host layer (Task 5 ships a stub; a real gateway integration is a later task).
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Captures payment for the given order.</summary>
    /// <param name="orderId">The identifier of the order being paid for.</param>
    /// <param name="amount">The amount to capture.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The outcome of the capture attempt.</returns>
    Task<PaymentProviderResult> CaptureAsync(Guid orderId, Money amount, CancellationToken ct);
}

/// <summary>
/// The outcome of a payment capture attempt against a payment provider.
/// </summary>
/// <param name="Success">Whether the capture succeeded.</param>
/// <param name="ProviderReference">The provider's reference for the captured payment, when successful.</param>
/// <param name="FailureReason">A human-readable reason for the failure, when unsuccessful.</param>
public sealed record PaymentProviderResult(bool Success, string? ProviderReference, string? FailureReason);

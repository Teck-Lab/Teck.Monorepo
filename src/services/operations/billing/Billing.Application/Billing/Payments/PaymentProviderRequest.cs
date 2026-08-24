namespace Billings.Application.Billing.Payments;

/// <summary>Describes one provider-neutral tokenized payment attempt.</summary>
/// <param name="OrderId">The order being charged.</param>
/// <param name="Amount">The platform-resolved amount.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="PaymentMethodToken">The opaque payment-method reference.</param>
/// <param name="RequestId">The stable idempotency key.</param>
public sealed record PaymentProviderRequest(Guid OrderId, decimal Amount, string Currency, string PaymentMethodToken, string RequestId);

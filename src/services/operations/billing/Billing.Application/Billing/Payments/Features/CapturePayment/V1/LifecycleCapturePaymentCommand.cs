using Billings.Application.Billing.Payments.Responses;
using ErrorOr;
using MemoryPack;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Payments.Features.CapturePayment.V1;

/// <summary>Starts a payment using values produced by the version-two checkout lifecycle.</summary>
/// <param name="OrderId">The order identifier.</param>
/// <param name="CustomerId">The checkout customer correlation, if available.</param>
/// <param name="Amount">The platform-resolved amount.</param>
/// <param name="Currency">The platform-resolved currency.</param>
[MemoryPackable]
public sealed partial record LifecycleCapturePaymentCommand(Guid OrderId, Guid CustomerId, decimal Amount, string Currency) : ICommand<ErrorOr<PaymentDto>>
{
    /// <summary>Gets the shopper-authorized amount ceiling.</summary>
    public decimal AuthorizedAmount { get; init; }

    /// <summary>Gets the opaque payment-method token.</summary>
    public string PaymentMethodToken { get; init; } = string.Empty;

    /// <summary>Gets the stable provider request identifier.</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>Gets the checkout correlation identifier.</summary>
    public string SourceCorrelationId { get; init; } = string.Empty;

    /// <summary>Validates the authority and idempotency data required by V2 lifecycle capture.</summary>
    /// <returns>An error when the V2 input must be rejected before any provider or persistence work.</returns>
    public Error? Validate()
    {
        if (Amount <= 0m || AuthorizedAmount <= 0m || Amount > AuthorizedAmount)
        {
            return Error.Validation("Payment.InvalidAuthority", "The payment amount must be positive and at or below the authorized amount.");
        }

        if (HasUnsupportedScale(Amount) || HasUnsupportedScale(AuthorizedAmount))
        {
            return Error.Validation("Payment.InvalidAmountScale", "Payment amounts support at most two decimal places.");
        }

        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3 || Currency.Any(character => character is < 'A' or > 'Z'))
        {
            return Error.Validation("Payment.InvalidCurrency", "Currency must be a three-letter uppercase ISO code.");
        }

        if (string.IsNullOrWhiteSpace(PaymentMethodToken) || PaymentMethodToken.Length > 256)
        {
            return Error.Validation("Payment.InvalidPaymentMethodToken", "Payment method token is required and must be at most 256 characters.");
        }

        if (string.IsNullOrWhiteSpace(RequestId) || RequestId.Length > 128)
        {
            return Error.Validation("Payment.InvalidRequestId", "Request identifier is required and must be at most 128 characters.");
        }

        return null;
    }

    private static bool HasUnsupportedScale(decimal value) => (decimal.GetBits(value)[3] >> 16 & 0x7f) > 2;
}

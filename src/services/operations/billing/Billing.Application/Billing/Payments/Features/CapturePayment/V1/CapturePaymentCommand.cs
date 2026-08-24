using Billings.Application.Billing.Payments.Responses;
using ErrorOr;
using MemoryPack;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Payments.Features.CapturePayment.V1;

/// <summary>Starts a payment using values produced by the checkout lifecycle.</summary>
/// <param name="OrderId">The order identifier.</param>
/// <param name="CustomerId">The legacy customer correlation, if available.</param>
/// <param name="Amount">The platform-resolved amount.</param>
/// <param name="Currency">The platform-resolved currency.</param>
[MemoryPackable]
public sealed partial record CapturePaymentCommand(Guid OrderId, Guid CustomerId, decimal Amount, string Currency) : ICommand<ErrorOr<PaymentDto>>
{
    /// <summary>Gets the shopper-authorized amount ceiling.</summary>
    public decimal AuthorizedAmount { get; init; }

    /// <summary>Gets the opaque payment-method token.</summary>
    public string PaymentMethodToken { get; init; } = string.Empty;

    /// <summary>Gets the stable provider request identifier.</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>Gets the checkout correlation identifier.</summary>
    public string SourceCorrelationId { get; init; } = string.Empty;

    /// <summary>Gets the effective authorized amount, preserving V1 compatibility.</summary>
    public decimal EffectiveAuthorizedAmount => AuthorizedAmount == 0m ? Amount : AuthorizedAmount;

    /// <summary>Gets the effective request identifier, preserving V1 compatibility.</summary>
    public string EffectiveRequestId => string.IsNullOrWhiteSpace(RequestId) ? $"legacy-{OrderId:N}" : RequestId;

    /// <summary>Gets the effective opaque token, preserving pre-flag V1 consumption only.</summary>
    public string EffectivePaymentMethodToken => string.IsNullOrWhiteSpace(PaymentMethodToken) ? "legacy-token" : PaymentMethodToken;
}

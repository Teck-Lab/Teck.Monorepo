namespace Billings.Application.Billing.Payments.Responses;

/// <summary>A payment.</summary>
/// <param name="Id">The payment identifier.</param>
/// <param name="OrderId">The identifier of the order this payment is for.</param>
/// <param name="CustomerId">The identifier of the customer making the payment.</param>
/// <param name="Amount">The payment amount.</param>
/// <param name="Currency">The ISO currency code of the payment amount.</param>
/// <param name="Status">The current lifecycle status of the payment.</param>
/// <param name="ProviderReference">The tokenized reference returned by the payment provider, if any.</param>
public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderReference);

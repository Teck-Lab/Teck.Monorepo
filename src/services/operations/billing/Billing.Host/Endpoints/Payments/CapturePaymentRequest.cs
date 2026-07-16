namespace Billings.Host.Endpoints.Payments;

/// <summary>Request to capture payment for an order.</summary>
/// <param name="OrderId">The identifier of the order being paid for.</param>
/// <param name="CustomerId">The identifier of the customer making the payment.</param>
/// <param name="Amount">The amount to capture.</param>
/// <param name="Currency">The ISO currency code of the amount to capture.</param>
public sealed record CapturePaymentRequest(Guid OrderId, Guid CustomerId, decimal Amount, string Currency);

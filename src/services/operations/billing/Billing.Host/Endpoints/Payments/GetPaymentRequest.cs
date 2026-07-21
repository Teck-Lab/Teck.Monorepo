namespace Billings.Host.Endpoints.Payments;

/// <summary>Request to fetch a payment by identifier.</summary>
/// <param name="PaymentId">The payment identifier.</param>
public sealed record GetPaymentRequest(Guid PaymentId);

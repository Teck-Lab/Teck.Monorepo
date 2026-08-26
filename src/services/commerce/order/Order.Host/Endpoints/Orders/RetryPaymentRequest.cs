namespace Orders.Host.Endpoints.Orders;

/// <summary>Requests a payment retry using an opaque replacement payment token.</summary>
/// <param name="OrderId">The order to retry, bound from the route.</param>
/// <param name="RequestId">The stable caller-generated retry request identifier.</param>
/// <param name="PaymentMethodToken">The bounded opaque replacement payment token.</param>
public sealed record RetryPaymentRequest(Guid OrderId, string RequestId, string PaymentMethodToken);

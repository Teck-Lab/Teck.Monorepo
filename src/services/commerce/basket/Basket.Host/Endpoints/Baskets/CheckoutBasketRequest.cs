namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to check out a basket.</summary>
/// <param name="BasketId">The basket to check out.</param>
/// <param name="AuthorizedAmount">The shopper-authorized maximum total.</param>
/// <param name="Currency">The authorization currency.</param>
/// <param name="PaymentReference">The bounded opaque tokenized payment reference.</param>
public sealed record CheckoutBasketRequest(Guid BasketId, decimal AuthorizedAmount, string Currency, string PaymentReference);

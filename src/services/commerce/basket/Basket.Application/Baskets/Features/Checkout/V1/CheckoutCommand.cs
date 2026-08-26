using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.Checkout.V1;

/// <summary>Command that checks out a basket, converting it toward an order.</summary>
/// <param name="BasketId">The basket to check out.</param>
/// <param name="AuthorizedAmount">The shopper-authorized maximum total.</param>
/// <param name="Currency">The ISO currency of the authorization.</param>
/// <param name="PaymentReference">The bounded opaque tokenized payment reference.</param>
public sealed record CheckoutCommand(Guid BasketId, decimal AuthorizedAmount, string Currency, string PaymentReference) : ICommand<BasketDto>;

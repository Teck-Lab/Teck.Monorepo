using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.Checkout.V1;

/// <summary>Command that checks out a basket, converting it toward an order.</summary>
/// <param name="BasketId">The basket to check out.</param>
public sealed record CheckoutCommand(Guid BasketId) : ICommand<BasketDto>;

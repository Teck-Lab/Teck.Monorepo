using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.ClearBasket.V1;

/// <summary>Command that removes all items from a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
public sealed record ClearBasketCommand(Guid BasketId) : ICommand<BasketDto>;

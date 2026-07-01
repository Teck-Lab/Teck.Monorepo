using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.RemoveItem.V1;

/// <summary>Command that removes a line from a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier to remove.</param>
public sealed record RemoveItemCommand(Guid BasketId, Guid ProductId) : ICommand<BasketDto>;

using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.UpdateItemQuantity.V1;

/// <summary>Command that sets the quantity of a basket line.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Quantity">The new quantity (zero or less removes the line).</param>
public sealed record UpdateItemQuantityCommand(Guid BasketId, Guid ProductId, int Quantity) : ICommand<BasketDto>;

using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.MergeBasket.V1;

/// <summary>Command that merges a guest basket into the authenticated customer's active basket.</summary>
/// <param name="AnonymousToken">The guest basket token to merge from.</param>
public sealed record MergeBasketCommand(Guid AnonymousToken) : ICommand<BasketDto>;

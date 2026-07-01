using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.GetOrCreateBasket.V1;

/// <summary>Command that returns the caller's active basket, creating one if none exists.</summary>
public sealed record GetOrCreateBasketCommand : ICommand<BasketDto>;

using Ardalis.Specification;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;

namespace Baskets.Application.Baskets.ReadModels;

/// <summary>Selects the active guest basket identified by an anonymous token, including its items.</summary>
public sealed class ActiveBasketByTokenSpec : Specification<Basket>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveBasketByTokenSpec"/> class.</summary>
    /// <param name="anonymousToken">The guest token.</param>
    public ActiveBasketByTokenSpec(Guid anonymousToken) =>
        Query.Where(basket => basket.AnonymousToken == anonymousToken && basket.Status == BasketStatus.Active)
            .Include(basket => basket.Items);
}

using Ardalis.Specification;
using Baskets.Domain.Entities;

namespace Baskets.Application.Baskets.ReadModels;

/// <summary>Selects a single basket by its identifier, including its items.</summary>
public sealed class BasketByIdSpec : Specification<Basket>
{
    /// <summary>Initializes a new instance of the <see cref="BasketByIdSpec"/> class.</summary>
    /// <param name="basketId">The basket identifier to match.</param>
    public BasketByIdSpec(Guid basketId) => Query.Where(basket => basket.Id == basketId).Include(basket => basket.Items);
}

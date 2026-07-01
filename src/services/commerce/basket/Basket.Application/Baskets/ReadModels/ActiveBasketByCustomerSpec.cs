using Ardalis.Specification;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;

namespace Baskets.Application.Baskets.ReadModels;

/// <summary>Selects the active basket owned by a customer, including its items.</summary>
public sealed class ActiveBasketByCustomerSpec : Specification<Basket>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveBasketByCustomerSpec"/> class.</summary>
    /// <param name="customerId">The owning customer identifier.</param>
    public ActiveBasketByCustomerSpec(Guid customerId) =>
        Query.Where(basket => basket.CustomerId == customerId && basket.Status == BasketStatus.Active)
            .Include(basket => basket.Items);
}

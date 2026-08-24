using Ardalis.Specification;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;

namespace Baskets.Application.Baskets.ReadModels;

/// <summary>Selects the active basket owned by an authenticated subject.</summary>
public sealed class ActiveBasketBySubjectSpec : Specification<Basket>
{
    /// <summary>Initializes the subject lookup.</summary>
    /// <param name="subject">The persisted authenticated subject.</param>
    public ActiveBasketBySubjectSpec(string subject) =>
        Query.Where(basket => basket.Subject == subject && basket.Status == BasketStatus.Active)
            .Include(basket => basket.Items);
}

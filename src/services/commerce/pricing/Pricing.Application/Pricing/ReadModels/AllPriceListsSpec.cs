using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects all price lists (ordered by name), including their prices.</summary>
public sealed class AllPriceListsSpec : Specification<PriceList>
{
    /// <summary>Initializes a new instance of the <see cref="AllPriceListsSpec"/> class.</summary>
    public AllPriceListsSpec() =>
        Query.Include(list => list.Prices).OrderBy(list => list.Name);
}

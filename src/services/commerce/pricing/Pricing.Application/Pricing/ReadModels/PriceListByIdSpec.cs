using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects a single price list by identifier, including its prices.</summary>
public sealed class PriceListByIdSpec : Specification<PriceList>
{
    /// <summary>Initializes a new instance of the <see cref="PriceListByIdSpec"/> class.</summary>
    /// <param name="id">The price list identifier.</param>
    public PriceListByIdSpec(Guid id) =>
        Query.Where(list => list.Id == id).Include(list => list.Prices);
}

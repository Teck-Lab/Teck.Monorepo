using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects all prices for a product, including their owning price list (scope/status/validity).</summary>
public sealed class PricesByProductSpec : Specification<Price>
{
    /// <summary>Initializes a new instance of the <see cref="PricesByProductSpec"/> class.</summary>
    /// <param name="productId">The product identifier.</param>
    public PricesByProductSpec(Guid productId) =>
        Query.Where(price => price.ProductId == productId).Include(price => price.PriceList);
}

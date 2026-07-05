using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;

namespace Pricing.Domain.Services;

/// <summary>
/// Selects the winning price for a resolution context: most-specific scope, native currency
/// preferred, with quantity-tier application. Pure and side-effect free; FX conversion of a
/// foreign winner is applied by the caller.
/// </summary>
public static class PriceResolutionService
{
    /// <summary>Selects the best price for the context, or null when none applies.</summary>
    /// <param name="candidates">Candidate prices for the product (each with its <see cref="Price.PriceList"/> loaded).</param>
    /// <param name="context">The resolution context.</param>
    /// <returns>The winning selection, or null.</returns>
    public static ResolvedSelection? SelectBest(IEnumerable<Price> candidates, PriceResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        List<Price> compatible = candidates
            .Where(price => price.PriceList is not null
                && price.PriceList.Status == PriceListStatus.Active
                && price.PriceList.IsValidAt(context.At)
                && price.PriceList.Scope.IsCompatibleWith(context.Country, context.CustomerGroupId, context.ChannelId))
            .ToList();

        if (compatible.Count == 0)
        {
            return null;
        }

        List<Price> native = compatible
            .Where(price => string.Equals(price.PriceList.Scope.Currency, context.Currency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<Price> pool = native.Count > 0 ? native : compatible;

        Price winner = pool
            .OrderByDescending(price => price.PriceList.Scope.Specificity)
            .ThenByDescending(price => price.PriceList.Scope.ChannelId is not null)
            .ThenByDescending(price => price.PriceList.Scope.CustomerGroupId is not null)
            .ThenByDescending(price => price.PriceList.Scope.Country is not null)
            .ThenBy(price => price.UnitAmountFor(context.Quantity).Amount)
            .ThenBy(price => price.PriceList.CreatedAt)
            .First();

        return new ResolvedSelection(winner, winner.UnitAmountFor(context.Quantity));
    }
}

using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;

namespace Pricing.Domain.Services;

/// <summary>The result of price selection: the winning price and its tiered unit amount (native currency).</summary>
/// <param name="Price">The winning price.</param>
/// <param name="UnitAmount">The tiered unit amount in the price's native currency.</param>
public sealed record ResolvedSelection(Price Price, Money UnitAmount);

// <copyright file="VolumePricingCalculator.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Calculates prorated costs for volume-tiered resources (ESL displays, access points, etc.).
/// </summary>
public static class VolumePricingCalculator
{
    /// <summary>
    /// Calculates the prorated monthly cost delta when increasing a resource from
    /// <paramref name="currentQuantity"/> to <paramref name="newQuantity"/>, using tiered pricing.
    /// </summary>
    /// <param name="currentQuantity">The current allocated quantity (baseline, already paid).</param>
    /// <param name="newQuantity">The requested new quantity (must be greater than current).</param>
    /// <param name="tiers">The pricing tiers, ordered by ascending <see cref="PricingTier.UpTo"/>.</param>
    /// <param name="daysRemaining">Remaining days on the current license period.</param>
    /// <returns>The prorated charge for the additional units over the remaining period.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="newQuantity"/> is not greater than <paramref name="currentQuantity"/>.</exception>
    public static decimal CalculateDelta(
        int currentQuantity,
        int newQuantity,
        IReadOnlyList<PricingTier> tiers,
        decimal daysRemaining)
    {
        ArgumentNullException.ThrowIfNull(tiers);

        if (newQuantity <= currentQuantity)
        {
            throw new ArgumentException(
                $"New quantity ({newQuantity}) must be greater than current quantity ({currentQuantity}).",
                nameof(newQuantity));
        }

        if (daysRemaining <= 0)
        {
            return 0m;
        }

        // Full monthly cost for new quantity minus full monthly cost for current quantity,
        // then prorated to remaining days.
        decimal fullMonthlyNew = CalculateMonthlyTotal(newQuantity, tiers);
        decimal fullMonthlyCurrent = CalculateMonthlyTotal(currentQuantity, tiers);
        decimal monthlyDelta = fullMonthlyNew - fullMonthlyCurrent;

        return monthlyDelta * (daysRemaining / 30m);
    }

    /// <summary>
    /// Calculates the prorated monthly cost for a full plan upgrade.
    /// </summary>
    /// <param name="currentPlan">The current tenant plan.</param>
    /// <param name="newPlan">The new (higher) tenant plan.</param>
    /// <param name="daysRemaining">Remaining days on the current license period.</param>
    /// <returns>The prorated charge for the plan upgrade over the remaining period.</returns>
    public static decimal CalculatePlanUpgradeDelta(
        TenantPlan currentPlan,
        TenantPlan newPlan,
        decimal daysRemaining)
    {
        ArgumentNullException.ThrowIfNull(currentPlan);
        ArgumentNullException.ThrowIfNull(newPlan);

        if (daysRemaining <= 0)
        {
            return 0m;
        }

        decimal monthlyDelta = newPlan.BasePrice - currentPlan.BasePrice;
        if (monthlyDelta <= 0)
        {
            return 0m;
        }

        return monthlyDelta * (daysRemaining / 30m);
    }

    /// <summary>
    /// Computes the total monthly cost for a given quantity using volume tiers.
    /// Each tier covers units from the previous tier's upper bound up to its own <see cref="PricingTier.UpTo"/>.
    /// </summary>
    private static decimal CalculateMonthlyTotal(int quantity, IReadOnlyList<PricingTier> tiers)
    {
        decimal total = 0m;
        int remaining = quantity;
        int previous = 0;

        foreach (PricingTier tier in tiers)
        {
            if (remaining <= 0)
            {
                break;
            }

            int tierCapacity = tier.UpTo == int.MaxValue
                ? remaining
                : Math.Min(remaining, tier.UpTo - previous);

            total += tierCapacity * tier.PricePerUnit;
            remaining -= tierCapacity;
            previous = tier.UpTo == int.MaxValue ? previous : tier.UpTo;
        }

        return total;
    }
}

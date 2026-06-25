// <copyright file="PricingTier.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents a single tier in a volume-based pricing table.
/// Units up to <see cref="UpTo"/> are billed at <see cref="PricePerUnit"/> per unit per month.
/// Use <see cref="int.MaxValue"/> for the final open-ended tier.
/// </summary>
/// <param name="UpTo">The inclusive upper bound of this tier (units).</param>
/// <param name="PricePerUnit">The price per unit per month for units falling in this tier.</param>
public sealed record PricingTier(int UpTo, decimal PricePerUnit);

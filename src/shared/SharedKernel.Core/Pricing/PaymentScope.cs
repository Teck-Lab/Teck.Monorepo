// <copyright file="PaymentScope.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Ardalis.SmartEnum;

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents who is responsible for payment on a license.
/// Serializes to its <c>Name</c> property so existing
/// persisted values ("TenantDefault", "LocationOverride") are preserved without migration.
/// </summary>
public sealed class PaymentScope : SmartEnum<PaymentScope>
{
    /// <summary>
    /// The tenant pays for this license (default for tenant-level licenses).
    /// </summary>
    public static readonly PaymentScope TenantDefault = new(nameof(TenantDefault), 1);

    /// <summary>
    /// The location overrides the tenant payment and pays for this license itself.
    /// </summary>
    public static readonly PaymentScope LocationOverride = new(nameof(LocationOverride), 2);

    private PaymentScope(string name, int value)
        : base(name, value)
    {
    }
}

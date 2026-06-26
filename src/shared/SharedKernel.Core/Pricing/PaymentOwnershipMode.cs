// <copyright file="PaymentOwnershipMode.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Ardalis.SmartEnum;

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Controls who is allowed to purchase and manage licenses for locations under a tenant.
/// This is a tenant-level setting.
/// </summary>
public sealed class PaymentOwnershipMode : SmartEnum<PaymentOwnershipMode>
{
    /// <summary>
    /// The tenant owns and pays for all location licenses.
    /// Locations cannot purchase additional capacity independently.
    /// </summary>
    public static readonly PaymentOwnershipMode TenantOwned = new(nameof(TenantOwned), 1);

    /// <summary>
    /// Each location owns and pays for its own license independently.
    /// The tenant does not provision base licenses for locations.
    /// </summary>
    public static readonly PaymentOwnershipMode LocationOwned = new(nameof(LocationOwned), 2);

    /// <summary>
    /// Hybrid model: the tenant provides a base license to each location (paid by tenant),
    /// and locations may purchase additional capacity on top (paid by the location).
    /// Tenant cannot modify location-purchased licenses; locations cannot modify tenant-provided licenses.
    /// </summary>
    public static readonly PaymentOwnershipMode Hybrid = new(nameof(Hybrid), 3);

    private PaymentOwnershipMode(string name, int value)
        : base(name, value)
    {
    }
}

// <copyright file="LicenseOwnershipType.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Ardalis.SmartEnum;

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents who created and is responsible for a specific license assigned to a location.
/// </summary>
public sealed class LicenseOwnershipType : SmartEnum<LicenseOwnershipType>
{
    /// <summary>
    /// The license was provisioned by the tenant and assigned to the location.
    /// Only the tenant may modify or revoke it.
    /// </summary>
    public static readonly LicenseOwnershipType TenantProvided = new(nameof(TenantProvided), 1);

    /// <summary>
    /// The license was purchased by the location itself.
    /// Only the location may modify or revoke it. Stacks on top of any tenant-provided license.
    /// </summary>
    public static readonly LicenseOwnershipType LocationPurchased = new(nameof(LocationPurchased), 2);

    private LicenseOwnershipType(string name, int value)
        : base(name, value)
    {
    }
}

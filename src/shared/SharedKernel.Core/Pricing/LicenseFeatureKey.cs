// <copyright file="LicenseFeatureKey.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Defines the canonical string keys used in license product features and additional attributes.
/// Use these constants everywhere license feature dictionaries are read or written to ensure type safety
/// and prevent key typos.
/// </summary>
public static class LicenseFeatureKey
{
    // -------------------------------------------------------------------------
    // Product feature keys (WithProductFeatures)
    // -------------------------------------------------------------------------

    /// <summary>Maximum number of access points per location. Value: integer or "unlimited".</summary>
    public const string AccessPointsMax = "access_points_max";

    /// <summary>Maximum number of ESL devices per location. Value: integer or "unlimited".</summary>
    public const string DevicesMax = "devices_max";

    /// <summary>Maximum number of products per location. Value: integer or "unlimited".</summary>
    public const string ProductsMax = "products_max";

    /// <summary>Maximum number of locations for the tenant. Value: integer.</summary>
    public const string MaxLocations = "max_locations";

    /// <summary>Whether custom branding is supported. Value: "True" or "False".</summary>
    public const string SupportsCustomBranding = "supports_custom_branding";

    /// <summary>Whether analytics features are available. Value: "True" or "False".</summary>
    public const string SupportsAnalytics = "supports_analytics";

    // -------------------------------------------------------------------------
    // Additional attribute keys (WithAdditionalAttributes)
    // -------------------------------------------------------------------------

    /// <summary>The tenant identifier this license was issued for.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>The location identifier this license applies to, or "tenant-level" for tenant-scoped licenses.</summary>
    public const string LocationId = "location_id";

    /// <summary>The plan name (e.g. "Shared", "Business", "Enterprise").</summary>
    public const string Plan = "plan";

    /// <summary>The payment scope — "TenantDefault" or "LocationOverride".</summary>
    public const string PaymentScope = "payment_scope";

    /// <summary>The payment method identifier used to pay for this license.</summary>
    public const string PaymentMethodId = "payment_method_id";

    /// <summary>The license ownership type — "TenantProvided" or "LocationPurchased".</summary>
    public const string OwnershipType = "ownership_type";
}

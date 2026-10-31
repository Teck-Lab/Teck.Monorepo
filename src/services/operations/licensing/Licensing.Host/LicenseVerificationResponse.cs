namespace Licensing.Host;

/// <summary>Only verified, nonrevoked and unexpired license entitlements; invalid results disclose no claims.</summary>
/// <param name="IsValid">Whether the license is currently valid for the tenant.</param>
/// <param name="LicenseId">The validated persisted license ID.</param>
/// <param name="SourceOrderId">The confirmed purchase order from which the license was issued.</param>
/// <param name="SubscriptionId">The paid subscription identity.</param>
/// <param name="PlanId">The signed plan identifier.</param>
/// <param name="PlanVersion">The signed plan version.</param>
/// <param name="ExpiresAt">The license expiry time.</param>
/// <param name="Features">The signed feature values.</param>
public sealed record LicenseVerificationResponse(
    bool IsValid,
    Guid? LicenseId = null,
    Guid? SourceOrderId = null,
    Guid? SubscriptionId = null,
    string? PlanId = null,
    string? PlanVersion = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyDictionary<string, string>? Features = null);

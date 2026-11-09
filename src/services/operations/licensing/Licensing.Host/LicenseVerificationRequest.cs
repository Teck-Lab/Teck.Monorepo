namespace Licensing.Host;

/// <summary>A signed document and the tenant for which it must be valid.</summary>
/// <param name="TenantId">The expected tenant ID.</param>
/// <param name="LicenseDocument">The signed license XML supplied by the caller.</param>
public sealed record LicenseVerificationRequest(Guid TenantId, string LicenseDocument);

namespace Licensing.Application;

/// <summary>Persisted signed license document and metadata.</summary>
public sealed record IssuedLicense(Guid LicenseId, Guid SourceOrderId, Guid SubscriptionId, Guid TenantId, string PlanId, string PlanVersion, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string LicenseDocument, string SigningKeyId, bool Revoked);

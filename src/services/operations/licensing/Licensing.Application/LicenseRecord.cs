namespace Licensing.Application;

/// <summary>Database representation of an issued license.</summary>
public sealed class LicenseRecord
{
    /// <summary>Gets or sets the signed license ID.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the originating confirmed purchase order.</summary>
    public Guid SourceOrderId { get; set; }
    /// <summary>Gets or sets the subscription idempotency key.</summary>
    public Guid SubscriptionId { get; set; }
    /// <summary>Gets or sets the owning tenant.</summary>
    public Guid TenantId { get; set; }
    /// <summary>Gets or sets the plan ID.</summary>
    public string PlanId { get; set; } = string.Empty;
    /// <summary>Gets or sets the immutable plan version.</summary>
    public string PlanVersion { get; set; } = string.Empty;
    /// <summary>Gets or sets issue time.</summary>
    public DateTimeOffset IssuedAt { get; set; }
    /// <summary>Gets or sets expiration time.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Gets or sets the signed XML license document.</summary>
    public string LicenseDocument { get; set; } = string.Empty;
    /// <summary>Gets or sets signing-key identifier.</summary>
    public string SigningKeyId { get; set; } = string.Empty;
    /// <summary>Gets or sets revocation state.</summary>
    public bool Revoked { get; set; }

    /// <summary>Creates a database row from an application model.</summary>
    /// <param name="license">The issued license to store.</param>
    /// <returns>The corresponding database row.</returns>
    public static LicenseRecord FromIssuedLicense(IssuedLicense license) => new()
    {
        Id = license.LicenseId,
        SourceOrderId = license.SourceOrderId,
        SubscriptionId = license.SubscriptionId,
        TenantId = license.TenantId,
        PlanId = license.PlanId,
        PlanVersion = license.PlanVersion,
        IssuedAt = license.IssuedAt,
        ExpiresAt = license.ExpiresAt,
        LicenseDocument = license.LicenseDocument,
        SigningKeyId = license.SigningKeyId,
        Revoked = license.Revoked,
    };

    /// <summary>Creates an application model from this row.</summary>
    /// <returns>The issued license value.</returns>
    public IssuedLicense ToIssuedLicense() => new(Id, SourceOrderId, SubscriptionId, TenantId, PlanId, PlanVersion, IssuedAt, ExpiresAt, LicenseDocument, SigningKeyId, Revoked);
}

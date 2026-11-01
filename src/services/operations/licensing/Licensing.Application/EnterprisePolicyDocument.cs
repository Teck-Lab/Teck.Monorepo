using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Licensing.Application;

/// <summary>Operator-issued enterprise policy claims and detached RSA-PSS signature.</summary>
public sealed record EnterprisePolicyDocument(
    string PolicyId,
    string PolicyVersion,
    Guid TenantId,
    Guid SubscriptionId,
    Guid SourceOrderId,
    int GracePeriodDays,
    int BackupRetentionDays,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    string LicenseKind,
    string SigningKeyId,
    string Signature)
{
    /// <summary>Creates a signed custom-enterprise policy document.</summary>
    /// <param name="tenantId">Bound tenant identity.</param>
    /// <param name="subscriptionId">Bound subscription identity.</param>
    /// <param name="sourceOrderId">Bound paid source order.</param>
    /// <param name="policyId">Operator-assigned policy ID.</param>
    /// <param name="policyVersion">Immutable policy version.</param>
    /// <param name="gracePeriodDays">Grace period in days.</param>
    /// <param name="backupRetentionDays">Backup retention in days.</param>
    /// <param name="validFromUtc">Inclusive validity start.</param>
    /// <param name="validUntilUtc">Exclusive validity end.</param>
    /// <param name="privateKey">RSA private signing key.</param>
    /// <param name="signingKeyId">Signing key identifier.</param>
    /// <returns>The signed policy.</returns>
    /// <exception cref="ArgumentException">Claims or signing key are invalid.</exception>
    public static EnterprisePolicyDocument Sign(
        Guid tenantId,
        Guid subscriptionId,
        Guid sourceOrderId,
        string policyId,
        string policyVersion,
        int gracePeriodDays,
        int backupRetentionDays,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        RSA privateKey,
        string signingKeyId = "enterprise-policy")
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (tenantId == Guid.Empty || subscriptionId == Guid.Empty || sourceOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(policyId) || string.IsNullOrWhiteSpace(policyVersion) ||
            gracePeriodDays < 0 || backupRetentionDays < 0 || validUntilUtc <= validFromUtc || privateKey.KeySize < 2048)
        {
            throw new ArgumentException("Enterprise policy claims or signing key are invalid.");
        }

        var unsigned = new EnterprisePolicyDocument(
            policyId,
            policyVersion,
            tenantId,
            subscriptionId,
            sourceOrderId,
            gracePeriodDays,
            backupRetentionDays,
            validFromUtc.ToUniversalTime(),
            validUntilUtc.ToUniversalTime(),
            "custom_enterprise",
            signingKeyId,
            string.Empty);
        return unsigned with
        {
            Signature = Convert.ToBase64String(privateKey.SignData(unsigned.Payload(), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)),
        };
    }

    /// <summary>Verifies signature, immutable purchase binding, policy kind and validity interval.</summary>
    /// <param name="tenantId">Expected tenant identity.</param>
    /// <param name="subscriptionId">Expected subscription identity.</param>
    /// <param name="sourceOrderId">Expected paid source order.</param>
    /// <param name="nowUtc">Verification time.</param>
    /// <param name="publicKey">Trusted RSA public key.</param>
    /// <returns>True only for a matching, valid signed policy.</returns>
    public bool Verify(Guid tenantId, Guid subscriptionId, Guid sourceOrderId, DateTimeOffset nowUtc, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (TenantId != tenantId || SubscriptionId != subscriptionId || SourceOrderId != sourceOrderId ||
            LicenseKind != "custom_enterprise" || ValidFromUtc.Offset != TimeSpan.Zero ||
            ValidUntilUtc.Offset != TimeSpan.Zero || nowUtc < ValidFromUtc || nowUtc >= ValidUntilUtc ||
            GracePeriodDays < 0 || BackupRetentionDays < 0)
        {
            return false;
        }

        try
        {
            return publicKey.VerifyData(Payload(), Convert.FromBase64String(Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private byte[] Payload() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        policy_id = PolicyId,
        policy_version = PolicyVersion,
        tenant_id = TenantId,
        subscription_id = SubscriptionId,
        source_order_id = SourceOrderId,
        grace_period_days = GracePeriodDays,
        backup_retention_days = BackupRetentionDays,
        valid_from_utc = ValidFromUtc.ToUniversalTime().ToString("O"),
        valid_until_utc = ValidUntilUtc.ToUniversalTime().ToString("O"),
        license_kind = LicenseKind,
        signing_key_id = SigningKeyId,
    }));
}

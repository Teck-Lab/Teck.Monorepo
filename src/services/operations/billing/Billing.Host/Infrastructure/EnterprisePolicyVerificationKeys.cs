using System.Security.Cryptography;
using Licensing.Host.Grpc.V1;

namespace Billings.Host.Infrastructure;

/// <summary>Verifies an active Licensing policy against the deployment's trusted public signing keys.</summary>
public sealed class EnterprisePolicyVerificationKeys : IDisposable
{
    private readonly Dictionary<string, RSA> keys;

    /// <summary>Loads trusted public PEM keys keyed by the Licensing signing key ID.</summary>
    /// <param name="configuration">Billing's immutable deployment configuration.</param>
    public EnterprisePolicyVerificationKeys(IConfiguration configuration)
    {
        keys = new Dictionary<string, RSA>(StringComparer.Ordinal);
        foreach (var section in configuration.GetSection("EnterprisePolicy:TrustedPublicKeys").GetChildren())
        {
            var keyId = section.Key;
            var pem = section.Value;
            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(pem) || !pem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Enterprise policy verification keys must be public PEM values keyed by key ID.");
            }

            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            if (rsa.KeySize is < 2048 or > 4096 || !keys.TryAdd(keyId, rsa))
            {
                rsa.Dispose();
                throw new InvalidOperationException("Enterprise policy verification key is invalid or duplicated.");
            }
        }
    }

    /// <summary>Returns true only for a cryptographically valid policy with exact claims and validity.</summary>
    /// <param name="policy">The signed Licensing policy document.</param>
    /// <param name="tenantId">The expected tenant identity.</param>
    /// <param name="subscriptionId">The expected subscription identity.</param>
    /// <param name="sourceOrderId">The expected paid source order identity.</param>
    /// <param name="now">The UTC instant for validity checks.</param>
    /// <returns>True only when the signature and all claims match.</returns>
    public bool Verify(EnterprisePolicyDocument policy, Guid tenantId, Guid subscriptionId, Guid sourceOrderId, DateTimeOffset now)
    {
        if (policy is null || !keys.TryGetValue(policy.SigningKeyId, out var rsa) ||
            !Guid.TryParse(policy.TenantId, out var actualTenant) || actualTenant != tenantId ||
            !Guid.TryParse(policy.SubscriptionId, out var actualSubscription) || actualSubscription != subscriptionId ||
            !Guid.TryParse(policy.SourceOrderId, out var actualOrder) || actualOrder != sourceOrderId ||
            policy.LicenseKind != "custom_enterprise" || string.IsNullOrWhiteSpace(policy.PolicyId) ||
            string.IsNullOrWhiteSpace(policy.PolicyVersion) || policy.GracePeriodDays is < 0 or > 3650 ||
            policy.BackupRetentionDays is < 0 or > 36500 ||
            !DateTimeOffset.TryParse(policy.ValidFromUtc, out var from) ||
            !DateTimeOffset.TryParse(policy.ValidUntilUtc, out var until) || from.Offset != TimeSpan.Zero ||
            until.Offset != TimeSpan.Zero || now < from || now >= until)
        {
            return false;
        }

        try
        {
            var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                policy_id = policy.PolicyId,
                policy_version = policy.PolicyVersion,
                tenant_id = tenantId,
                backup_retention_days = policy.BackupRetentionDays,
                license_kind = policy.LicenseKind,
                signing_key_id = policy.SigningKeyId,
            });
            return rsa.VerifyData(payload, Convert.FromBase64String(policy.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var key in keys.Values)
        {
            key.Dispose();
        }
    }
}

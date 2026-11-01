using System.Security.Cryptography;

namespace SharedKernel.Events;

/// <summary>Verifies signed Licensing documents reconstructed from attached transitions.</summary>
public sealed class EnterprisePolicyDocumentVerificationKeys : IDisposable
{
    private readonly Dictionary<string, RSA> keys = new(StringComparer.Ordinal);

    /// <summary>Loads trusted Licensing public keys keyed by signing key ID.</summary>
    /// <param name="configuredKeys">Trusted public PEM keys keyed by signing key ID.</param>
    public EnterprisePolicyDocumentVerificationKeys(IEnumerable<KeyValuePair<string, string?>>? configuredKeys)
    {
        foreach (var entry in configuredKeys ?? Array.Empty<KeyValuePair<string, string?>>())
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value) ||
                !entry.Value.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Licensing verification keys must be public PEM values keyed by signing key ID.");
            }

            var key = RSA.Create();
            key.ImportFromPem(entry.Value);
            if (key.KeySize is < 2048 or > 4096 || !keys.TryAdd(entry.Key, key))
            {
                key.Dispose();
                throw new InvalidOperationException("Licensing verification key size is unsupported or key ID duplicated.");
            }
        }

        if (keys.Count == 0)
        {
            throw new InvalidOperationException("No Licensing policy verification keys were configured.");
        }
    }

    /// <summary>Verifies the detached Licensing signature and current validity interval.</summary>
    /// <param name="transition">Transition carrying the signed Licensing document.</param>
    /// <param name="nowUtc">The current UTC instant.</param>
    /// <returns>True when signature and validity interval match.</returns>
    public bool Verify(EnterprisePolicyAttachedIntegrationEvent transition, DateTimeOffset nowUtc)
    {
        if (transition is null || !keys.TryGetValue(transition.LicensingSigningKeyId, out var key) ||
            transition.LicenseKind != "custom_enterprise" || transition.GracePeriodDays is < 0 or > 3650 ||
            transition.BackupRetentionDays is < 0 or > 36500 || transition.ValidFromUtc.Offset != TimeSpan.Zero ||
            transition.ValidUntilUtc.Offset != TimeSpan.Zero || transition.ValidUntilUtc <= transition.ValidFromUtc ||
            nowUtc < transition.ValidFromUtc || nowUtc >= transition.ValidUntilUtc)
        {
            return false;
        }

        try
        {
            var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                policy_id = transition.PolicyId,
                policy_version = transition.PolicyVersion,
                tenant_id = transition.TenantId,
                subscription_id = transition.SubscriptionId,
                source_order_id = transition.SourceOrderId,
                grace_period_days = transition.GracePeriodDays,
                backup_retention_days = transition.BackupRetentionDays,
                valid_from_utc = transition.ValidFromUtc.ToUniversalTime().ToString("O"),
                valid_until_utc = transition.ValidUntilUtc.ToUniversalTime().ToString("O"),
                license_kind = transition.LicenseKind,
                signing_key_id = transition.LicensingSigningKeyId,
            });
            return key.VerifyData(payload, Convert.FromBase64String(transition.LicensingSignature), HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
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

        keys.Clear();
    }
}

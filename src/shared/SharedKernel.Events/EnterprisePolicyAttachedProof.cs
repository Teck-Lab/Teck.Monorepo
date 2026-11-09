using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Events;

/// <summary>Signs and verifies policy attachment transitions using Billing's payment-attestation key.</summary>
public static class EnterprisePolicyAttachedProof
{
    /// <summary>Signs a complete transition.</summary>
    /// <param name="transition">Transition claims signed by Billing.</param>
    /// <param name="privateKey">Billing-only RSA private key.</param>
    /// <returns>Base64 RSA-PSS SHA-256 signature.</returns>
    public static string Sign(EnterprisePolicyAttachedIntegrationEvent transition, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        Validate(transition, requireProof: false);
        return Convert.ToBase64String(privateKey.SignData(Payload(transition), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    /// <summary>Authenticates a transition.</summary>
    /// <param name="transition">Received transition claims.</param>
    /// <param name="publicKey">Trusted Billing RSA public key.</param>
    /// <returns>True when the signature matches every claim.</returns>
    public static bool Verify(EnterprisePolicyAttachedIntegrationEvent transition, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        try
        {
            Validate(transition, requireProof: true);
            return publicKey.VerifyData(Payload(transition), Convert.FromBase64String(transition.Proof), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static void Validate(EnterprisePolicyAttachedIntegrationEvent e, bool requireProof)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.TenantId == Guid.Empty || e.SubscriptionId == Guid.Empty || e.SourceOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(e.TenantIdentifier) || string.IsNullOrWhiteSpace(e.PolicyId) ||
            string.IsNullOrWhiteSpace(e.PolicyVersion) || e.GracePeriodDays is < 0 or > 3650 ||
            e.BackupRetentionDays is < 0 or > 36500 || string.IsNullOrWhiteSpace(e.LicensingSigningKeyId) ||
            e.LicenseKind != "custom_enterprise" || string.IsNullOrWhiteSpace(e.LicensingSignature) ||
            e.ValidFromUtc.Offset != TimeSpan.Zero || e.ValidUntilUtc.Offset != TimeSpan.Zero || e.ValidUntilUtc <= e.ValidFromUtc ||
            (requireProof && string.IsNullOrWhiteSpace(e.Proof)))
        {
            throw new ArgumentException("A complete enterprise policy transition is required.", nameof(e));
        }
    }

    private static byte[] Payload(EnterprisePolicyAttachedIntegrationEvent e)
    {
        var writer = new ArrayBufferWriter<byte>();
        Add(e.TenantId.ToString("D"));
        Add(e.TenantIdentifier);
        Add(e.SubscriptionId.ToString("D"));
        Add(e.SourceOrderId.ToString("D"));
        Add(e.PolicyId);
        Add(e.PolicyVersion);
        Add(e.GracePeriodDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(e.BackupRetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(e.LicenseKind);
        Add(e.LicensingSigningKeyId);
        Add(e.LicensingSignature);
        Add(e.ValidFromUtc.ToString("O"));
        Add(e.ValidUntilUtc.ToString("O"));
        return writer.WrittenSpan.ToArray();

        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(BitConverter.GetBytes(bytes.Length));
            writer.Write(bytes);
        }
    }
}

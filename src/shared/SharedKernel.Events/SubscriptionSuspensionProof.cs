using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Events;

/// <summary>Domain-separated RSA-PSS proof over the immutable Billing subscription suspension.</summary>
public static class SubscriptionSuspensionProof
{
    /// <summary>Signs a cancellation or failed renewal with Billing's private key.</summary>
    /// <param name="suspension">The source order, subscription, tenant and effective time.</param>
    /// <param name="privateKey">The Billing-only private RSA key.</param>
    /// <returns>A detached base64 signature.</returns>
    public static string Sign(SubscriptionAccessSuspendedIntegrationEvent suspension, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (!TryReason(suspension, out byte reason) || privateKey.KeySize is < 2048 or > 4096)
        {
            throw new ArgumentException("A complete Billing subscription suspension and supported private key are required.");
        }

        Span<byte> payload = stackalloc byte[512];
        int used = WritePayload(suspension, reason, payload);
        Span<byte> signature = stackalloc byte[512];
        if (!privateKey.TrySignData(payload[..used], signature, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss, out int written))
        {
            throw new CryptographicException("Billing could not sign the subscription suspension.");
        }

        return Convert.ToBase64String(signature[..written]);
    }

    /// <summary>Rejects forged, changed or malformed cancellation broker messages.</summary>
    /// <param name="suspension">The received subscription suspension.</param>
    /// <param name="publicKey">One trusted Billing public RSA key.</param>
    /// <returns>True only when Billing signed this exact tenant, subscription, reason and time.</returns>
    public static bool Verify(SubscriptionAccessSuspendedIntegrationEvent suspension, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (!TryReason(suspension, out byte reason) || publicKey.KeySize is < 2048 or > 4096 ||
            string.IsNullOrEmpty(suspension.Proof) || suspension.Proof.Length > 2048)
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[512];
        if (!Convert.TryFromBase64String(suspension.Proof, signature, out int written) ||
            written != publicKey.KeySize / 8)
        {
            return false;
        }

        Span<byte> payload = stackalloc byte[512];
        int used = WritePayload(suspension, reason, payload);
        return publicKey.VerifyData(payload[..used], signature[..written],
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    private static bool TryReason(SubscriptionAccessSuspendedIntegrationEvent suspension, out byte reason)
    {
        reason = suspension?.Reason switch { "cancelled" => (byte)1, "renewal_failed" => (byte)2, _ => (byte)0 };
        return reason != 0 && suspension!.TenantId != Guid.Empty && suspension.SourceOrderId != Guid.Empty &&
            suspension.SubscriptionId != Guid.Empty && suspension.SuspendedAtUtc != default &&
            !string.IsNullOrWhiteSpace(suspension.TenantIdentifier) &&
            Encoding.UTF8.GetByteCount(suspension.TenantIdentifier) <= 256;
    }

    private static int WritePayload(SubscriptionAccessSuspendedIntegrationEvent suspension, byte reason, Span<byte> payload)
    {
        ReadOnlySpan<byte> purpose = "teck.billing.subscription-access-suspended.v1"u8;
        purpose.CopyTo(payload);
        int offset = purpose.Length;
        suspension.TenantId.TryWriteBytes(payload[offset..]);
        offset += 16;
        suspension.SourceOrderId.TryWriteBytes(payload[offset..]);
        offset += 16;
        suspension.SubscriptionId.TryWriteBytes(payload[offset..]);
        offset += 16;
        BinaryPrimitives.WriteInt64LittleEndian(payload[offset..], suspension.SuspendedAtUtc.UtcDateTime.Ticks);
        offset += 8;
        payload[offset++] = reason;
        int count = Encoding.UTF8.GetBytes(suspension.TenantIdentifier.AsSpan(), payload[(offset + 2)..]);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[offset..], (ushort)count);
        return offset + 2 + count;
    }
}

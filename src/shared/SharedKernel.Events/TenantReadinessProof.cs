using System.Buffers;
using System.Security.Cryptography;

namespace SharedKernel.Events;

/// <summary>Authenticates readiness emitted by the actual licensing or database provisioner.</summary>
public static class TenantReadinessProof
{
    private const int DigestLength = 32;

    /// <summary>Signs a correlated, persisted readiness result with that provisioner's private shared key.</summary>
    /// <param name="readiness">The immutable readiness payload including a nonempty evidence ID.</param>
    /// <param name="key">A deployment-supplied key containing at least 32 random bytes.</param>
    /// <returns>The lowercase HMAC-SHA256 digest for the event.</returns>
    public static string Sign(TenantProvisioningReadyIntegrationEvent readiness, ReadOnlySpan<byte> key)
    {
        if (key.Length < DigestLength || !TryPayload(readiness, out var provisioner))
        {
            throw new ArgumentException("A valid correlated readiness result and 256-bit signing key are required.");
        }

        Span<byte> payload = stackalloc byte[66];
        WritePayload(readiness, provisioner, payload);
        Span<byte> digest = stackalloc byte[DigestLength];
        HMACSHA256.HashData(key, payload, digest);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>Verifies producer evidence without trusting a caller-supplied provisioner label alone.</summary>
    /// <param name="readiness">The received readiness event.</param>
    /// <param name="key">The configured key for the specified provisioner only.</param>
    /// <returns>True only for a matching correlated, authenticated event.</returns>
    public static bool Verify(TenantProvisioningReadyIntegrationEvent readiness, ReadOnlySpan<byte> key)
    {
        if (key.Length < DigestLength || !TryPayload(readiness, out var provisioner) ||
            readiness.Proof.Length != DigestLength * 2)
        {
            return false;
        }

        Span<byte> received = stackalloc byte[DigestLength];
        if (Convert.FromHexString(readiness.Proof.AsSpan(), received, out int consumed, out int written) != OperationStatus.Done ||
            consumed != readiness.Proof.Length || written != DigestLength)
        {
            return false;
        }

        Span<byte> payload = stackalloc byte[66];
        WritePayload(readiness, provisioner, payload);
        Span<byte> expected = stackalloc byte[DigestLength];
        HMACSHA256.HashData(key, payload, expected);
        return CryptographicOperations.FixedTimeEquals(received, expected);
    }

    private static bool TryPayload(TenantProvisioningReadyIntegrationEvent readiness, out byte provisioner)
    {
        provisioner = readiness.Provisioner switch { "license" => (byte)1, "database" => (byte)2, _ => (byte)0 };
        return provisioner != 0 && readiness.TenantId != Guid.Empty && readiness.SourceOrderId != Guid.Empty &&
            readiness.SubscriptionId != Guid.Empty && readiness.EvidenceId != Guid.Empty;
    }

    private static void WritePayload(TenantProvisioningReadyIntegrationEvent readiness, byte provisioner, Span<byte> destination)
    {
        destination[0] = 1; // Domain-separated proof version.
        readiness.TenantId.TryWriteBytes(destination[1..17]);
        readiness.SourceOrderId.TryWriteBytes(destination[17..33]);
        readiness.SubscriptionId.TryWriteBytes(destination[33..49]);
        readiness.EvidenceId.TryWriteBytes(destination[49..65]);
        destination[65] = provisioner;
    }
}

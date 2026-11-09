using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Events;

/// <summary>Customer-only signing and worker-only verification of a two-operator tenant purge decision.</summary>
public static class TenantPurgeApprovalProof
{
    /// <summary>Signs an approved decision with Customer's dedicated purge private key.</summary>
    /// <param name="approval">The complete audited, immutable decision.</param>
    /// <param name="privateKey">Customer-only 2048–4096-bit RSA private key.</param>
    /// <returns>Base64 detached RSA-PSS signature.</returns>
    public static string Sign(TenantPurgeApprovedIntegrationEvent approval, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (!IsComplete(approval) || privateKey.KeySize is < 2048 or > 4096)
        {
            throw new ArgumentException("A complete two-person paid tenant purge approval is required.");
        }

        Span<byte> payload = stackalloc byte[1024];
        int used = WritePayload(approval, payload);
        Span<byte> signature = stackalloc byte[512];
        if (!privateKey.TrySignData(payload[..used], signature, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss, out int written))
        {
            throw new CryptographicException("Customer could not attest the tenant purge approval.");
        }

        return Convert.ToBase64String(signature[..written]);
    }

    /// <summary>Rejects forged, incomplete, different-tenant or changed-source-order purge decisions.</summary>
    /// <param name="approval">The received broker decision.</param>
    /// <param name="publicKey">A trusted Customer purge public key, never the Billing payment key.</param>
    /// <returns>True only for the exact signed two-person decision.</returns>
    public static bool Verify(TenantPurgeApprovedIntegrationEvent approval, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (!IsComplete(approval) || publicKey.KeySize is < 2048 or > 4096 ||
            string.IsNullOrEmpty(approval.Proof) || approval.Proof.Length > 2048)
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[512];
        if (!Convert.TryFromBase64String(approval.Proof, signature, out int written) ||
            written != publicKey.KeySize / 8)
        {
            return false;
        }

        Span<byte> payload = stackalloc byte[1024];
        int used = WritePayload(approval, payload);
        return publicKey.VerifyData(payload[..used], signature[..written],
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    private static bool IsComplete(TenantPurgeApprovedIntegrationEvent? approval) =>
        approval is not null && approval.ApprovalId != Guid.Empty && approval.TenantId != Guid.Empty &&
        approval.SubscriptionId != Guid.Empty && approval.SourceOrderId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(approval.TenantIdentifier) &&
        !string.IsNullOrWhiteSpace(approval.FirstApproverSubjectId) &&
        !string.IsNullOrWhiteSpace(approval.SecondApproverSubjectId) &&
        !string.Equals(approval.FirstApproverSubjectId, approval.SecondApproverSubjectId, StringComparison.Ordinal) &&
        Encoding.UTF8.GetByteCount(approval.TenantIdentifier) <= 256 &&
        Encoding.UTF8.GetByteCount(approval.FirstApproverSubjectId) <= 128 &&
        Encoding.UTF8.GetByteCount(approval.SecondApproverSubjectId) <= 128 &&
        approval.SuspendedAtUtc != default && approval.EligibleAtUtc > approval.SuspendedAtUtc &&
        approval.ApprovedAtUtc >= approval.EligibleAtUtc;

    private static int WritePayload(TenantPurgeApprovedIntegrationEvent approval, Span<byte> payload)
    {
        ReadOnlySpan<byte> purpose = "teck.customer.tenant-purge-approved.v1"u8;
        purpose.CopyTo(payload);
        int offset = purpose.Length;
        approval.ApprovalId.TryWriteBytes(payload[offset..]); offset += 16;
        approval.TenantId.TryWriteBytes(payload[offset..]); offset += 16;
        approval.SubscriptionId.TryWriteBytes(payload[offset..]); offset += 16;
        approval.SourceOrderId.TryWriteBytes(payload[offset..]); offset += 16;
        BinaryPrimitives.WriteInt64LittleEndian(payload[offset..], approval.SuspendedAtUtc.UtcDateTime.Ticks); offset += 8;
        BinaryPrimitives.WriteInt64LittleEndian(payload[offset..], approval.EligibleAtUtc.UtcDateTime.Ticks); offset += 8;
        BinaryPrimitives.WriteInt64LittleEndian(payload[offset..], approval.ApprovedAtUtc.UtcDateTime.Ticks); offset += 8;
        offset = WriteString(approval.TenantIdentifier, payload, offset);
        offset = WriteString(approval.FirstApproverSubjectId, payload, offset);
        return WriteString(approval.SecondApproverSubjectId, payload, offset);
    }

    private static int WriteString(string value, Span<byte> payload, int offset)
    {
        int count = Encoding.UTF8.GetBytes(value.AsSpan(), payload[(offset + 2)..]);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[offset..], (ushort)count);
        return offset + 2 + count;
    }
}

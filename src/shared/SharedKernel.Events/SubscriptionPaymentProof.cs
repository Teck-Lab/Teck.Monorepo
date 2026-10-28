using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Events;

/// <summary>Authenticates Billing's complete immutable paid snapshot without sharing its private key with consumers.</summary>
public static class SubscriptionPaymentProof
{
    /// <summary>Signs a verified captured charge or audited invoice with Billing's RSA-PSS private key.</summary>
    /// <param name="paid">The immutable server-priced subscription snapshot.</param>
    /// <param name="privateKey">The Billing-only private signing key.</param>
    /// <returns>A base64 RSA-PSS SHA-256 signature.</returns>
    public static string Sign(SubscriptionPaymentConfirmedIntegrationEvent paid, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (!IsValidSnapshot(paid) || privateKey.KeySize is < 2048 or > 4096)
        {
            throw new ArgumentException("A complete confirmed subscription and supported Billing signing key are required.");
        }

        var bytes = CreatePayload(paid);
        Span<byte> signature = stackalloc byte[512];
        if (!privateKey.TrySignData(bytes.WrittenSpan, signature, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss, out int written))
        {
            throw new CryptographicException("Billing could not sign the confirmed subscription.");
        }

        return Convert.ToBase64String(signature[..written]);
    }

    /// <summary>Rejects untrusted broker messages and any changed pricing, entitlement or tenant fields.</summary>
    /// <param name="paid">The received paid subscription snapshot.</param>
    /// <param name="publicKey">One deployment-approved Billing public key.</param>
    /// <returns>True only for a complete, byte-for-byte authenticated snapshot.</returns>
    public static bool Verify(SubscriptionPaymentConfirmedIntegrationEvent paid, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (!IsValidSnapshot(paid) || publicKey.KeySize is < 2048 or > 4096 ||
            string.IsNullOrEmpty(paid.Proof) || paid.Proof.Length > 2048)
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[512];
        if (!Convert.TryFromBase64String(paid.Proof, signature, out int written) ||
            written != publicKey.KeySize / 8)
        {
            return false;
        }

        var bytes = CreatePayload(paid);
        return publicKey.VerifyData(bytes.WrittenSpan, signature[..written],
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    private static bool IsValidSnapshot(SubscriptionPaymentConfirmedIntegrationEvent paid) =>
        paid is not null && paid.SubscriptionId != Guid.Empty && paid.TenantId != Guid.Empty &&
        paid.SourceOrderId != Guid.Empty && Fits(paid.TenantIdentifier) &&
        Fits(paid.PlanId) && Fits(paid.PlanVersion) &&
        paid.Features is { Count: <= 64 } && paid.Features.All(static feature =>
            Fits(feature.Key) && Fits(feature.Value)) &&
        paid.AmountMinor >= 0 && Fits(paid.Currency) &&
        Fits(paid.DatabaseStrategy) && Fits(paid.DatabaseProvider) &&
        paid.PaymentMethod is "captured" or "external_invoice" &&
        paid.ValidFromUtc != default && paid.ValidUntilUtc > paid.ValidFromUtc &&
        paid.ConfirmedAtUtc != default;

    private static bool Fits(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value) <= 4096;

    private static ArrayBufferWriter<byte> CreatePayload(SubscriptionPaymentConfirmedIntegrationEvent paid)
    {
        var writer = new ArrayBufferWriter<byte>(512);
        WriteString(writer, "teck.billing.subscription-payment-confirmed.v2");
        WriteGuid(writer, paid.SubscriptionId);
        WriteGuid(writer, paid.TenantId);
        WriteString(writer, paid.TenantIdentifier);
        WriteGuid(writer, paid.SourceOrderId);
        WriteString(writer, paid.PlanId);
        WriteString(writer, paid.PlanVersion);

        var featureKeys = paid.Features.Keys.ToArray();
        Array.Sort(featureKeys, StringComparer.Ordinal);
        WriteInt32(writer, featureKeys.Length);
        foreach (string featureKey in featureKeys)
        {
            WriteString(writer, featureKey);
            WriteString(writer, paid.Features[featureKey]);
        }

        WriteNullableInt32(writer, paid.MaxAccessPointsPerLocation);
        WriteNullableInt32(writer, paid.MaxDevicesPerLocation);
        WriteNullableInt32(writer, paid.MaxProductsPerLocation);
        WriteInt32(writer, paid.MaxLocations);
        WriteBoolean(writer, paid.SupportsCustomBranding);
        WriteBoolean(writer, paid.SupportsAnalytics);
        WriteInt64(writer, paid.AmountMinor);
        WriteString(writer, paid.Currency);
        WriteString(writer, paid.DatabaseStrategy);
        WriteString(writer, paid.DatabaseProvider);
        WriteBoolean(writer, paid.HasReadReplicas);
        WriteInt32(writer, paid.ReplicaCount);
        WriteString(writer, paid.PaymentMethod);
        WriteInt64(writer, paid.ConfirmedAtUtc.UtcDateTime.Ticks);
        WriteInt64(writer, paid.ValidFromUtc.UtcDateTime.Ticks);
        WriteInt64(writer, paid.ValidUntilUtc.UtcDateTime.Ticks);
        WriteGuid(writer, paid.PreviousSourceOrderId);
        return writer;
    }

    private static void WriteGuid(IBufferWriter<byte> writer, Guid value)
    {
        value.TryWriteBytes(writer.GetSpan(16));
        writer.Advance(16);
    }

    private static void WriteString(IBufferWriter<byte> writer, string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        if (count > 4096)
        {
            throw new ArgumentException("Subscription snapshot contains an oversized field.");
        }

        Span<byte> target = writer.GetSpan(4 + count);
        BinaryPrimitives.WriteInt32LittleEndian(target, count);
        Encoding.UTF8.GetBytes(value.AsSpan(), target[4..]);
        writer.Advance(4 + count);
    }

    private static void WriteNullableInt32(IBufferWriter<byte> writer, int? value)
    {
        WriteBoolean(writer, value.HasValue);
        if (value is { } number)
        {
            WriteInt32(writer, number);
        }
    }

    private static void WriteBoolean(IBufferWriter<byte> writer, bool value)
    {
        writer.GetSpan(1)[0] = value ? (byte)1 : (byte)0;
        writer.Advance(1);
    }

    private static void WriteInt32(IBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), value);
        writer.Advance(4);
    }

    private static void WriteInt64(IBufferWriter<byte> writer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
    }
}

using SharedKernel.Events;

namespace Licensing.Application;

/// <summary>Signs licensing readiness using the deployment-managed, licensing-only shared secret.</summary>
public sealed class ReadinessSigningKey
{
    private readonly byte[] key;

    /// <summary>Decodes the configured 256-bit-or-stronger random key.</summary>
    /// <param name="base64Key">The runtime secret in base64; never stored in source or database.</param>
    public ReadinessSigningKey(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException("Licensing readiness signing key is unavailable.");
        }

        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Licensing readiness signing key is malformed.", exception);
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException("Licensing readiness signing key is too short.");
        }
    }

    /// <summary>Signs the persisted license ID and correlated order, tenant and subscription.</summary>
    /// <param name="readiness">The completed licensing readiness result.</param>
    /// <returns>A proof that Customer can verify with the corresponding issuer key.</returns>
    public string Sign(TenantProvisioningReadyIntegrationEvent readiness) => TenantReadinessProof.Sign(readiness, key);
}

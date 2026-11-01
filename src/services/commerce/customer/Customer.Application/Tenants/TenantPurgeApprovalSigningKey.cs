using System.Security.Cryptography;
using SharedKernel.Events;

namespace Customers.Application.Tenants;

/// <summary>Customer-only private signing authority, isolated from Billing payment and database readiness keys.</summary>
public sealed class TenantPurgeApprovalSigningKey : IDisposable
{
    private readonly RSA privateKey = RSA.Create();

    /// <summary>Loads Customer's encrypted PKCS#8 purge approval key from runtime secrets.</summary>
    /// <param name="encryptedPrivateKey">Encrypted PKCS#8 PEM document.</param>
    /// <param name="passphrase">Separately delivered secret passphrase.</param>
    public TenantPurgeApprovalSigningKey(string? encryptedPrivateKey, string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(encryptedPrivateKey) || string.IsNullOrWhiteSpace(passphrase))
        {
            privateKey.Dispose();
            throw new InvalidOperationException("Customer purge approval signing key is unavailable.");
        }

        try
        {
            privateKey.ImportFromEncryptedPem(encryptedPrivateKey, passphrase);
            if (privateKey.KeySize is < 2048 or > 4096)
            {
                throw new InvalidOperationException("Customer purge approval key size is unsupported.");
            }
        }
        catch
        {
            privateKey.Dispose();
            throw;
        }
    }

    /// <summary>Signs only the complete paid-period, tenant-specific two-person approval.</summary>
    /// <param name="approval">The decision to attest.</param>
    /// <returns>Detached RSA-PSS signature.</returns>
    public string Sign(TenantPurgeApprovedIntegrationEvent approval) => TenantPurgeApprovalProof.Sign(approval, privateKey);

    /// <inheritdoc/>
    public void Dispose() => privateKey.Dispose();
}

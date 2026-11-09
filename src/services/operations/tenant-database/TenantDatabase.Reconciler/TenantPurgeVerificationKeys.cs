using System.Security.Cryptography;
using SharedKernel.Events;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Worker-only trusted Customer purge public keys, separate from Billing's payment authority.</summary>
public sealed class TenantPurgeVerificationKeys : IDisposable
{
    private readonly List<RSA> publicKeys = [];

    /// <summary>Loads active and rotation-overlap SubjectPublicKeyInfo PEM keys.</summary>
    /// <param name="trustedPublicKeys">Approved Customer purge public key documents.</param>
    public TenantPurgeVerificationKeys(IEnumerable<string>? trustedPublicKeys)
    {
        RSA? pending = null;
        try
        {
            foreach (string pem in trustedPublicKeys ?? [])
            {
                if (pem is null || !pem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Customer purge verification keys must be public PEM documents.");
                }

                pending = RSA.Create();
                pending.ImportFromPem(pem);
                if (pending.KeySize is < 2048 or > 4096)
                {
                    throw new InvalidOperationException("Customer purge verification key size is unsupported.");
                }

                publicKeys.Add(pending);
                pending = null;
            }

            if (publicKeys.Count == 0)
            {
                throw new InvalidOperationException("No Customer purge approval verification keys were configured.");
            }
        }
        catch
        {
            pending?.Dispose();
            Dispose();
            throw;
        }
    }

    /// <summary>Checks an unchanged tenant-specific, two-operator approval against trusted public keys.</summary>
    /// <param name="approval">Received signed Customer decision.</param>
    /// <returns>True only for a valid Customer signature.</returns>
    public bool Verify(TenantPurgeApprovedIntegrationEvent approval)
    {
        foreach (RSA key in publicKeys)
        {
            if (TenantPurgeApprovalProof.Verify(approval, key)) return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (RSA key in publicKeys) key.Dispose();
        publicKeys.Clear();
    }
}

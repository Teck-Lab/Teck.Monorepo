using System.Security.Cryptography;

namespace SharedKernel.Events;

/// <summary>Consumer-held Billing public keys; never contains a payment-signing private key.</summary>
public sealed class SubscriptionPaymentVerificationKeys : IDisposable
{
    private readonly List<RSA> publicKeys = [];

    /// <summary>Loads the active and, during rotation, previously trusted public keys.</summary>
    /// <param name="trustedPublicKeys">Runtime SubjectPublicKeyInfo PEM documents.</param>
    public SubscriptionPaymentVerificationKeys(IEnumerable<string>? trustedPublicKeys)
    {
        var completed = false;
        RSA? pending = null;
        try
        {
            foreach (string pem in trustedPublicKeys ?? Array.Empty<string>())
            {
                if (pem is null || !pem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A Billing payment verification key must be a public PEM document.");
                }

                pending = RSA.Create();
                pending.ImportFromPem(pem);
                if (pending.KeySize is < 2048 or > 4096)
                {
                    throw new InvalidOperationException("Billing payment verification key size is unsupported.");
                }

                publicKeys.Add(pending);
                pending = null;
            }

            if (publicKeys.Count == 0)
            {
                throw new InvalidOperationException("No Billing payment verification keys were configured.");
            }

            completed = true;
        }
        finally
        {
            pending?.Dispose();
            if (!completed)
            {
                Dispose();
            }
        }
    }

    /// <summary>Checks the complete paid snapshot against every currently trusted Billing public key.</summary>
    /// <param name="paid">The received broker message.</param>
    /// <returns>True only for a valid Billing signature on the unmodified snapshot.</returns>
    public bool Verify(SubscriptionPaymentConfirmedIntegrationEvent paid)
    {
        foreach (var key in publicKeys)
        {
            if (SubscriptionPaymentProof.Verify(paid, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Checks a cancelled or failed-renewal subscription with public-only Billing keys.</summary>
    /// <param name="suspension">The received subscription suspension.</param>
    /// <returns>True only when Billing signed this exact suspended tenant and subscription.</returns>
    public bool Verify(SubscriptionAccessSuspendedIntegrationEvent suspension)
    {
        foreach (var key in publicKeys)
        {
            if (SubscriptionSuspensionProof.Verify(suspension, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var key in publicKeys)
        {
            key.Dispose();
        }

        publicKeys.Clear();
    }
}

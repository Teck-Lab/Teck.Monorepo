using System.Security.Cryptography;

namespace SharedKernel.Events;

/// <summary>Consumer-held Billing public keys; never contains a payment-signing private key.</summary>
public sealed class SubscriptionPaymentVerificationKeys : IDisposable
{
    private readonly List<RSA> publicKeys = [];

    /// <summary>Loads active and previously trusted public keys.</summary>
    /// <param name="trustedPublicKeys">SubjectPublicKeyInfo PEM documents.</param>
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

    /// <summary>Checks the complete paid snapshot against every trusted Billing key.</summary>
    /// <param name="paid">The received paid snapshot.</param>
    /// <returns>True only for an authenticated snapshot.</returns>
    public bool Verify(SubscriptionPaymentConfirmedIntegrationEvent paid) => publicKeys.Any(key => SubscriptionPaymentProof.Verify(paid, key));

    /// <summary>Checks a suspension against every trusted Billing key.</summary>
    /// <param name="suspension">The received suspension.</param>
    /// <returns>True only for an authenticated suspension.</returns>
    public bool Verify(SubscriptionAccessSuspendedIntegrationEvent suspension) => publicKeys.Any(key => SubscriptionSuspensionProof.Verify(suspension, key));

    /// <summary>Checks a policy transition against every trusted Billing key.</summary>
    /// <param name="transition">The received policy attachment.</param>
    /// <returns>True only for an authenticated transition.</returns>
    public bool Verify(EnterprisePolicyAttachedIntegrationEvent transition) => publicKeys.Any(key => EnterprisePolicyAttachedProof.Verify(transition, key));

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

using System.Security.Cryptography;

namespace SharedKernel.Events;

/// <summary>Billing-only encrypted private key for attesting verified payment snapshots.</summary>
public sealed class SubscriptionPaymentSigningKey : IDisposable
{
    private readonly RSA privateKey = RSA.Create();

    /// <summary>Loads the encrypted private key from runtime secret storage.</summary>
    /// <param name="encryptedPrivateKey">The encrypted PKCS#8 PEM document.</param>
    /// <param name="passphrase">The separately delivered private-key passphrase.</param>
    public SubscriptionPaymentSigningKey(string? encryptedPrivateKey, string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(encryptedPrivateKey) || string.IsNullOrWhiteSpace(passphrase))
        {
            throw new InvalidOperationException("Billing payment signing key is unavailable.");
        }

        try
        {
            privateKey.ImportFromEncryptedPem(encryptedPrivateKey, passphrase);
            if (privateKey.KeySize is < 2048 or > 4096)
            {
                throw new InvalidOperationException("Billing payment signing key size is unsupported.");
            }
        }
        catch
        {
            privateKey.Dispose();
            throw;
        }
    }

    /// <summary>Signs only the immutable captured-payment or audited-invoice snapshot.</summary>
    /// <param name="paid">The server-priced confirmed subscription.</param>
    /// <returns>A detached RSA-PSS proof.</returns>
    public string Sign(SubscriptionPaymentConfirmedIntegrationEvent paid) => SubscriptionPaymentProof.Sign(paid, privateKey);

    /// <summary>Signs an access suspension without letting consumers mint cancellation events.</summary>
    /// <param name="suspension">The correlated subscription suspension.</param>
    /// <returns>A detached RSA-PSS proof.</returns>
    public string Sign(SubscriptionAccessSuspendedIntegrationEvent suspension) => SubscriptionSuspensionProof.Sign(suspension, privateKey);

    /// <inheritdoc />
    public void Dispose() => privateKey.Dispose();
}

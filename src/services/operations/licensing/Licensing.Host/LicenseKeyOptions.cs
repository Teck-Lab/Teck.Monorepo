using Licensing.Application;

namespace Licensing.Host;

/// <summary>Secret-backed active signing key configuration.</summary>
public sealed class LicenseKeyOptions : ILicenseSigningKeyProvider
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "LicenseSigning";
    /// <summary>Signing key identifier used to select published public-key metadata.</summary>
    public string KeyId { get; set; } = string.Empty;
    /// <summary>Encrypted private key; store in a deployment secret, never a committed settings file.</summary>
    public string EncryptedPrivateKey { get; set; } = string.Empty;
    /// <summary>Passphrase for the encrypted private key; store in secret storage.</summary>
    public string Passphrase { get; set; } = string.Empty;

    /// <inheritdoc/>
    public Task<LicenseSigningKey?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(KeyId) || string.IsNullOrWhiteSpace(EncryptedPrivateKey) || string.IsNullOrWhiteSpace(Passphrase))
        {
            return Task.FromResult<LicenseSigningKey?>(null);
        }

        return Task.FromResult<LicenseSigningKey?>(new LicenseSigningKey(KeyId, EncryptedPrivateKey, Passphrase));
    }
}

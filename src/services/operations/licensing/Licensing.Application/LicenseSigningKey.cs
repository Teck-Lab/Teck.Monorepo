namespace Licensing.Application;

/// <summary>Private signing key material from secret storage.</summary>
public sealed record LicenseSigningKey(string KeyId, string EncryptedPrivateKey, string Passphrase);

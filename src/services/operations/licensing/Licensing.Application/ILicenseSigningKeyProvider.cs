namespace Licensing.Application;

/// <summary>Provides the active private signing key.</summary>
public interface ILicenseSigningKeyProvider
{
    /// <summary>Gets the active signing key, or null when unavailable.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active key, or null when secret material is absent.</returns>
    Task<LicenseSigningKey?> GetCurrentAsync(CancellationToken cancellationToken);
}

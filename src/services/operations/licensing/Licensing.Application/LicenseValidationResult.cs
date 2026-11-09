namespace Licensing.Application;

/// <summary>Consumer-facing license validation response.</summary>
public sealed record LicenseValidationResult(bool IsValid, string? Error, IssuedLicense? License, IReadOnlyDictionary<string, string>? Features)
{
    /// <summary>Creates a rejected validation result.</summary>
    /// <param name="error">The validation failure reason.</param>
    /// <returns>A rejected result with no license claims.</returns>
    public static LicenseValidationResult Invalid(string error) => new(false, error, null, null);

    /// <summary>Creates a successful validation result.</summary>
    /// <param name="license">The validated persisted license.</param>
    /// <param name="features">The signed feature claims.</param>
    /// <returns>A valid result with its license and features.</returns>
    public static LicenseValidationResult Valid(IssuedLicense license, IReadOnlyDictionary<string, string> features) => new(true, null, license, features);
}

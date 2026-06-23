namespace SharedKernel.Infrastructure.FeatureFlags;

/// <summary>
/// Feature flag options.
/// </summary>
public sealed class FeatureFlagOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string Section = "FeatureFlags";

    /// <summary>
    /// Gets or sets the default provider name.
    /// </summary>
    public string ProviderName { get; set; } = "in-memory";

    /// <summary>
    /// Gets or sets the OpenFeature provider endpoint.
    /// </summary>
    public Uri? OpenFeatureProviderEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the flag values.
    /// </summary>
    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

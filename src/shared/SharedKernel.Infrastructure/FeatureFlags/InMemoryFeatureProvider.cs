using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace SharedKernel.Infrastructure.FeatureFlags;

/// <summary>
/// Minimal feature provider contract used by the lightweight OpenFeature-compatible implementation.
/// </summary>
public interface IFeatureProvider
{
    /// <summary>
    /// Determines whether the specified flag is enabled.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">The default value when the flag is not configured.</param>
    /// <returns><see langword="true"/> when enabled; otherwise, <see langword="false"/>.</returns>
    bool IsEnabled(string flagKey, bool defaultValue = false);

    /// <summary>
    /// Sets or overrides the specified flag in memory.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="enabled">The value to set.</param>
    void SetFlag(string flagKey, bool enabled);
}

/// <summary>
/// Lightweight in-memory feature flag provider.
/// </summary>
public sealed class InMemoryFeatureProvider : IFeatureProvider
{
    private readonly IOptionsMonitor<FeatureFlagOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, bool> _overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryFeatureProvider"/> class.
    /// </summary>
    /// <param name="optionsMonitor">The feature flag options monitor.</param>
    public InMemoryFeatureProvider(IOptionsMonitor<FeatureFlagOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    /// <summary>
    /// Determines whether the specified flag is enabled.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">The default value when the flag is not configured.</param>
    /// <returns><see langword="true"/> when enabled; otherwise, <see langword="false"/>.</returns>
    public bool IsEnabled(string flagKey, bool defaultValue = false)
    {
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return defaultValue;
        }

        if (_overrides.TryGetValue(flagKey, out var overrideValue))
        {
            return overrideValue;
        }

        return _optionsMonitor.CurrentValue.Flags.TryGetValue(flagKey, out var configuredValue)
            ? configuredValue
            : defaultValue;
    }

    /// <summary>
    /// Sets or overrides the specified flag in memory.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="enabled">The value to set.</param>
    public void SetFlag(string flagKey, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        _overrides[flagKey] = enabled;
    }
}

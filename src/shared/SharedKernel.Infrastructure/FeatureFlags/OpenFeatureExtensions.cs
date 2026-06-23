using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Infrastructure.FeatureFlags;

/// <summary>
/// Feature flag registration and evaluation extensions.
/// </summary>
public static class OpenFeatureExtensions
{
    /// <summary>
    /// Registers Teck feature flag support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddTeckFeatureFlags(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FeatureFlagOptions>()
            .Bind(configuration.GetSection(FeatureFlagOptions.Section))
            .ValidateOnStart();

        services.AddSingleton<InMemoryFeatureProvider>();
        services.AddSingleton<IFeatureProvider>(sp => sp.GetRequiredService<InMemoryFeatureProvider>());

        return services;
    }

    /// <summary>
    /// Evaluates a feature flag from DI.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">The default value when the flag is not configured.</param>
    /// <returns><see langword="true"/> when enabled; otherwise, <see langword="false"/>.</returns>
    public static bool IsEnabled(this IServiceProvider serviceProvider, string flagKey, bool defaultValue = false)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return serviceProvider.GetRequiredService<IFeatureProvider>().IsEnabled(flagKey, defaultValue);
    }
}

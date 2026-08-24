using Microsoft.Extensions.Configuration;
using SharedKernel.Infrastructure.FeatureFlags;

namespace Inventories.Application.Inventory;

/// <summary>Registers Inventory feature flags, including the frozen deployment-key compatibility binding.</summary>
public static class InventoryFeatureFlagExtensions
{
    /// <summary>
    /// Registers the Inventory feature provider with the <c>FeatureFlags__CheckoutLifecycleV2</c>
    /// deployment value mapped to the provider's canonical <c>CheckoutLifecycleV2</c> flag.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddInventoryFeatureFlags(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string? lifecycleV2 = configuration["FeatureFlags:CheckoutLifecycleV2"];
        if (lifecycleV2 is null)
        {
            return services.AddTeckFeatureFlags(configuration);
        }

        IConfiguration providerConfiguration = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:Flags:CheckoutLifecycleV2"] = lifecycleV2,
            })
            .Build();
        return services.AddTeckFeatureFlags(providerConfiguration);
    }
}

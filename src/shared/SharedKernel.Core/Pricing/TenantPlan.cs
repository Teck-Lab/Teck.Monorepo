using Ardalis.SmartEnum;

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents the available tenant plans.
/// </summary>
public sealed class TenantPlan : SmartEnum<TenantPlan>
{
    /// <summary>
    /// Gets the trial duration applied to trial plans.
    /// </summary>
    public static readonly TimeSpan TrialDuration = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets the database strategy associated with this tenant plan.
    /// </summary>
    public DatabaseStrategy DatabaseStrategy { get; }

    /// <summary>
    /// Gets the base price for this tenant plan.
    /// </summary>
    public decimal BasePrice { get; }

    /// <summary>
    /// Gets a description of the plan.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the maximum number of access points per location for this plan, or null for unlimited.
    /// </summary>
    public int? MaxAccessPointsPerLocation { get; }

    /// <summary>
    /// Gets the maximum number of devices per location for this plan, or null for unlimited.
    /// </summary>
    public int? MaxDevicesPerLocation { get; }

    /// <summary>
    /// Gets the maximum number of products per location for this plan, or null for unlimited.
    /// </summary>
    public int? MaxProductsPerLocation { get; }

    /// <summary>
    /// Gets the maximum number of locations for this plan.
    /// </summary>
    public int MaxLocations { get; }

    /// <summary>
    /// Gets a value indicating whether this plan is a trial plan.
    /// </summary>
    public bool IsTrial { get; }

    /// <summary>
    /// Gets a value indicating whether this plan supports custom branding.
    /// </summary>
    public bool SupportsCustomBranding { get; }

    /// <summary>
    /// Gets a value indicating whether this plan supports analytics.
    /// </summary>
    public bool SupportsAnalytics { get; }

    /// <summary>
    /// No plan assigned.
    /// </summary>
    public static readonly TenantPlan None = new(nameof(None), 0, DatabaseStrategy.None, 0m, "No plan assigned.", maxAccessPointsPerLocation: 5, maxDevicesPerLocation: 10, maxProductsPerLocation: 50, maxLocations: 1, isTrial: false, supportsCustomBranding: false, supportsAnalytics: false);

    /// <summary>
    /// Shared tenant plan.
    /// </summary>
    public static readonly TenantPlan Shared = new(nameof(Shared), 1, DatabaseStrategy.Shared, 29.99m, "Shared plan for entry-level tenants.", maxAccessPointsPerLocation: 10, maxDevicesPerLocation: 50, maxProductsPerLocation: 200, maxLocations: 3, isTrial: false, supportsCustomBranding: false, supportsAnalytics: false);

    /// <summary>
    /// Premium tenant plan.
    /// </summary>
    public static readonly TenantPlan Premium = new(nameof(Premium), 2, DatabaseStrategy.Dedicated, 99.99m, "Premium plan with dedicated resources.", maxAccessPointsPerLocation: 50, maxDevicesPerLocation: 500, maxProductsPerLocation: 1000, maxLocations: 10, isTrial: false, supportsCustomBranding: true, supportsAnalytics: true);

    /// <summary>
    /// Business/Professional tenant plan (between Premium and Enterprise).
    /// </summary>
    public static readonly TenantPlan Business = new(nameof(Business), 3, DatabaseStrategy.Dedicated, 149.99m, "Business plan for professional tenants.", maxAccessPointsPerLocation: 200, maxDevicesPerLocation: 2000, maxProductsPerLocation: 5000, maxLocations: 25, isTrial: false, supportsCustomBranding: true, supportsAnalytics: true);

    /// <summary>
    /// Enterprise tenant plan.
    /// </summary>
    public static readonly TenantPlan Enterprise = new(nameof(Enterprise), 4, DatabaseStrategy.External, 299.99m, "Enterprise plan with external database.", maxAccessPointsPerLocation: null, maxDevicesPerLocation: null, maxProductsPerLocation: null, maxLocations: int.MaxValue, isTrial: false, supportsCustomBranding: true, supportsAnalytics: true);

    /// <summary>
    /// Trial tenant plan.
    /// </summary>
    public static readonly TenantPlan Trial = new(nameof(Trial), 5, DatabaseStrategy.Shared, 0m, "Trial plan for evaluation.", maxAccessPointsPerLocation: 2, maxDevicesPerLocation: 10, maxProductsPerLocation: 100, maxLocations: 1, isTrial: true, supportsCustomBranding: false, supportsAnalytics: false);

    /// <summary>
    /// Inactive or archived tenant plan.
    /// </summary>
    public static readonly TenantPlan InactiveArchived = new(nameof(InactiveArchived), 6, DatabaseStrategy.Shared, 0m, "Inactive or archived plan.", maxAccessPointsPerLocation: 0, maxDevicesPerLocation: 0, maxProductsPerLocation: 0, maxLocations: 0, isTrial: false, supportsCustomBranding: false, supportsAnalytics: false);

    private TenantPlan(string name, int value, DatabaseStrategy databaseStrategy, decimal basePrice, string description, int? maxAccessPointsPerLocation, int? maxDevicesPerLocation, int? maxProductsPerLocation, int maxLocations, bool isTrial, bool supportsCustomBranding, bool supportsAnalytics) : base(name, value)
    {
        DatabaseStrategy = databaseStrategy;
        BasePrice = basePrice;
        Description = description;
        MaxAccessPointsPerLocation = maxAccessPointsPerLocation;
        MaxDevicesPerLocation = maxDevicesPerLocation;
        MaxProductsPerLocation = maxProductsPerLocation;
        MaxLocations = maxLocations;
        IsTrial = isTrial;
        SupportsCustomBranding = supportsCustomBranding;
        SupportsAnalytics = supportsAnalytics;
    }

    /// <summary>
    /// Gets the database strategy associated with a given tenant plan.
    /// </summary>
    /// <param name="plan">The tenant plan.</param>
    /// <returns>The corresponding <see cref="DatabaseStrategy"/>.</returns>
    public static DatabaseStrategy GetDatabaseStrategyFromPlan(TenantPlan plan)
        => plan?.DatabaseStrategy ?? DatabaseStrategy.Shared;

    /// <summary>
    /// Gets the base price for a given tenant plan.
    /// </summary>
    /// <param name="plan">The tenant plan.</param>
    /// <returns>The base price as a decimal.</returns>
    public static decimal GetBasePriceForPlan(TenantPlan plan)
        => plan?.BasePrice ?? 0.00m;
}

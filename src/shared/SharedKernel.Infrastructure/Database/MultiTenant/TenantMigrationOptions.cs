namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Configuration options for tenant migration behavior.
/// </summary>
public class TenantMigrationOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the Customer API is required for dedicated tenant migrations.
    /// If true, dedicated tenant migrations will fail if the Customer API is unavailable.
    /// </summary>
    public bool RequireCustomerApiForDedicatedTenants { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the Customer API is required for external tenant migrations.
    /// If true, external tenant migrations will fail if the Customer API is unavailable.
    /// </summary>
    public bool RequireCustomerApiForExternalTenants { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to fail migration if tenant information is missing or invalid.
    /// If false, will log warnings and skip the tenant.
    /// </summary>
    public bool FailOnMissingTenantInfo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to allow fallback to cached connection information
    /// when the Customer API is unavailable for premium/enterprise tenants.
    /// </summary>
    public bool AllowCachedConnectionForPremiumTenants { get; set; }

    /// <summary>
    /// Gets or sets the timeout for Customer API calls when resolving tenant connections.
    /// </summary>
    public TimeSpan CustomerApiTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the number of retry attempts for Customer API calls.
    /// </summary>
    public int CustomerApiRetryAttempts { get; set; } = 2;
}

namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Options for configuring the TeckCloud multi-tenant functionality.
/// </summary>
public class TeckCloudMultiTenancyOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether whether to use claim-based tenant resolution (default: true).
    /// </summary>
    public bool UseClaimStrategy { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether whether to use header-based tenant resolution.
    /// Service hosts always disable this: tenant authority is derived from signed claims only.
    /// </summary>
    public bool UseHeaderStrategy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether whether to use distributed cache store (default: true)
    /// This is only used when UseCustomerApiTenantStore is false.
    /// </summary>
    public bool UseDistributedCacheStore { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether whether to use Customer API for tenant details (default: false).
    /// </summary>
    public bool UseCustomerApiTenantStore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether whether to use distributed cache with the Customer API store (default: false)
    /// When true, tenant details from the Customer API will be stored in the distributed cache
    /// instead of memory cache.
    /// </summary>
    public bool UseDistributedCacheWithCustomerApi { get; set; }

    /// <summary>
    /// Gets or sets the name of the claim that contains the tenant ID (default: "tenant_id").
    /// </summary>
    public string TenantIdClaimName { get; set; } = "tenant_id";

    /// <summary>
    /// Gets or sets the name of the claim that contains multiple tenant IDs (default: "tenant_ids")
    /// Used when a user belongs to multiple tenants.
    /// </summary>
    public string MultiTenantClaimName { get; set; } = "tenant_ids";

    /// <summary>
    /// Gets or sets the name of the claim that contains the organization information (default: "organization").
    /// </summary>
    public string OrganizationClaimName { get; set; } = "organization";

    /// <summary>
    /// Gets or sets the name of the HTTP header for tenant ID (default: "X-TenantId").
    /// </summary>
    public string TenantIdHeaderName { get; set; } = "X-TenantId";

    /// <summary>
    /// Gets or sets the name of the HTTP header for tenant name (default: "X-TenantName").
    /// </summary>
    public string TenantNameHeaderName { get; set; } = "X-TenantName";

    /// <summary>
    /// Gets or sets the separator character for multiple tenant IDs in claims or headers (default: ",").
    /// </summary>
    public string TenantIdSeparator { get; set; } = ",";

    /// <summary>
    /// Gets or sets the strategy to use when multiple tenant IDs are available (default: First).
    /// </summary>
    public MultiTenantResolutionStrategy MultiTenantResolutionStrategy { get; set; } = MultiTenantResolutionStrategy.First;

    /// <summary>
    /// Gets or sets customer API tenant details options.
    /// </summary>
    public CustomerApiTenantOptions CustomerApiOptions { get; set; } = new();
}

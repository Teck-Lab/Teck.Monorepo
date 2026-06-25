namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Options for configuring the Customer API tenant resolution.
/// </summary>
public class CustomerApiTenantOptions
{
    /// <summary>
    /// Gets or sets the API endpoint to retrieve tenant details.
    /// </summary>
    public string TenantDetailsEndpoint { get; set; } = "api/tenants/{tenantId}";

    /// <summary>
    /// Gets or sets the API endpoint to retrieve all tenants.
    /// </summary>
    public string AllTenantsEndpoint { get; set; } = "api/tenants";

    /// <summary>
    /// Gets or sets the API endpoint to retrieve tenant by ID.
    /// </summary>
    public string TenantByIdEndpoint { get; set; } = "api/tenants/id/{id}";

    /// <summary>
    /// Gets or sets the API endpoint to retrieve tenant by name.
    /// </summary>
    public string TenantByNameEndpoint { get; set; } = "api/tenants/name/{name}";

    /// <summary>
    /// Gets or sets cache duration for tenant details in minutes.
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 30;

    /// <summary>
    /// Gets or sets the name of the HTTP client to use (default: "CustomerApi").
    /// </summary>
    public string HttpClientName { get; set; } = "CustomerApi";
}

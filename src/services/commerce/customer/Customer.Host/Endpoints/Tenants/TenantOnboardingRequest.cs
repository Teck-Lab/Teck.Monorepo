namespace Customers.Host.Endpoints.Tenants;

/// <summary>Request to reserve a tenant for the authenticated business owner.</summary>
public sealed class TenantOnboardingRequest
{
    /// <summary>Gets or sets the business display name used for the generated identifier.</summary>
    public string DisplayName { get; set; } = string.Empty;
}

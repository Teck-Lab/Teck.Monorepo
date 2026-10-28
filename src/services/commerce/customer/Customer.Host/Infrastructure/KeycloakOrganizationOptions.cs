namespace Customers.Host.Infrastructure;

/// <summary>Runtime Keycloak organization administration configuration.</summary>
public sealed class KeycloakOrganizationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "KeycloakOrganizations";

    /// <summary>Gets or sets the Keycloak base URL.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the target realm.</summary>
    public string Realm { get; set; } = "teck";

    /// <summary>Gets or sets the admin client identifier.</summary>
    public string AdminClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the admin client secret injected at runtime.</summary>
    public string AdminClientSecret { get; set; } = string.Empty;
}

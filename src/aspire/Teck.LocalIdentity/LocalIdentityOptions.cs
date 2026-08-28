namespace Teck.LocalIdentity;

/// <summary>Configuration for the local Keycloak realm reconciliation command.</summary>
public sealed class LocalIdentityOptions
{
    /// <summary>Gets or sets the Keycloak base address.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Gets or sets the Keycloak administrator user name.</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>Gets or sets the local Keycloak administrator password.</summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>Gets or sets the path to the committed Keycloak realm manifest.</summary>
    public string ManifestPath { get; set; } = "src/aspire/Teck.AppHost/realms/teck-realm.json";

    /// <summary>Gets or sets the path to the committed local organization manifest.</summary>
    public string OrganizationManifestPath { get; set; } = "src/aspire/Teck.AppHost/identity/local-organizations.json";

    /// <summary>Gets or sets the Keycloak realm that owns the local organizations.</summary>
    public string Realm { get; set; } = "teck";

    /// <summary>Validates configuration required to call the Keycloak administration API.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a required configuration value is absent.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(AdminUsername);
        if (string.IsNullOrWhiteSpace(AdminPassword))
        {
            throw new InvalidOperationException("Keycloak__AdminPassword must be configured with a local-only secret.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(OrganizationManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(Realm);
    }
}

using System.Text.Json;

namespace Teck.LocalIdentity;

/// <summary>Coordinates organization reconciliation before the dependent tenant-registry write.</summary>
public sealed class LocalIdentityProvisioner
{
    private readonly IOrganizationReconciler organizationReconciler;
    private readonly ITenantRegistryWriter tenantRegistryWriter;

    /// <summary>Initializes the ordered local identity provisioning coordinator.</summary>
    /// <param name="organizationReconciler">The Keycloak organization reconciler.</param>
    /// <param name="tenantRegistryWriter">The dependent Customer tenant registry writer.</param>
    public LocalIdentityProvisioner(IOrganizationReconciler organizationReconciler, ITenantRegistryWriter tenantRegistryWriter)
    {
        ArgumentNullException.ThrowIfNull(organizationReconciler);
        ArgumentNullException.ThrowIfNull(tenantRegistryWriter);
        this.organizationReconciler = organizationReconciler;
        this.tenantRegistryWriter = tenantRegistryWriter;
    }

    /// <summary>Reconciles organizations and then writes their tenant registry records.</summary>
    /// <param name="manifest">The committed local organization manifest.</param>
    /// <param name="cancellationToken">The token used to cancel provisioning.</param>
    /// <returns>The organizations with their generated Keycloak identifiers.</returns>
    public async Task<IReadOnlyList<ProvisionedOrganization>> ProvisionAsync(JsonDocument manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        IReadOnlyList<ProvisionedOrganization> organizations = await organizationReconciler
            .ReconcileAsync(manifest, cancellationToken)
            .ConfigureAwait(false);
        EnsureCompleteOrganizationSet(manifest, organizations);
        await tenantRegistryWriter.UpsertAsync(organizations, cancellationToken).ConfigureAwait(false);
        return organizations;
    }

    private static void EnsureCompleteOrganizationSet(JsonDocument manifest, IReadOnlyList<ProvisionedOrganization> organizations)
    {
        if (!manifest.RootElement.TryGetProperty("organizations", out JsonElement definitions) || definitions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Local organization manifest property 'organizations' must be an array.");
        }

        var expected = new HashSet<(string Alias, string TenantIdentifier)>();
        foreach (JsonElement definition in definitions.EnumerateArray())
        {
            string alias = RequiredString(definition, "alias");
            if (!definition.TryGetProperty("tenant", out JsonElement tenant))
            {
                throw new InvalidOperationException("Local organization manifest property 'tenant' is required.");
            }

            if (!expected.Add((alias, RequiredString(tenant, "identifier"))))
            {
                throw new InvalidOperationException("Local organization manifest contains duplicate alias and tenant identifier pairs.");
            }
        }

        var actual = new HashSet<(string Alias, string TenantIdentifier)>();
        if (organizations.Count != expected.Count || organizations.Any(organization =>
            organization.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(organization.Alias) ||
            string.IsNullOrWhiteSpace(organization.TenantIdentifier) ||
            !actual.Add((organization.Alias, organization.TenantIdentifier))) ||
            !actual.SetEquals(expected))
        {
            throw new InvalidOperationException("Keycloak organization reconciliation did not return the complete manifest organization set.");
        }
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"Local organization manifest property '{property}' is required.");
}

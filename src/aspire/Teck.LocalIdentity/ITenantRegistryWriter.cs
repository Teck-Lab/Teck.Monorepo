namespace Teck.LocalIdentity;

/// <summary>Upserts Customer tenant-registry records after organization reconciliation succeeds.</summary>
public interface ITenantRegistryWriter
{
    /// <summary>Upserts one tenant registry record for each provisioned organization.</summary>
    /// <param name="organizations">The organizations with generated Keycloak identifiers.</param>
    /// <param name="cancellationToken">The token used to cancel database calls.</param>
    /// <returns>A task that completes after the single database commit.</returns>
    Task UpsertAsync(IReadOnlyList<ProvisionedOrganization> organizations, CancellationToken cancellationToken);
}

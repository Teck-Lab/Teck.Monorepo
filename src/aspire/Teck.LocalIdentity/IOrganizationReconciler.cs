using System.Text.Json;

namespace Teck.LocalIdentity;

/// <summary>Reconciles Keycloak organizations and returns the resulting tenant-registration inputs.</summary>
public interface IOrganizationReconciler
{
    /// <summary>Reconciles organizations from the supplied manifest.</summary>
    /// <param name="manifest">The committed local organization manifest.</param>
    /// <param name="cancellationToken">The token used to cancel API calls.</param>
    /// <returns>The reconciled organizations with their generated Keycloak identifiers.</returns>
    Task<IReadOnlyList<ProvisionedOrganization>> ReconcileAsync(JsonDocument manifest, CancellationToken cancellationToken);
}

namespace Customers.Application.EventHandlers.IntegrationEvents;

/// <summary>Links a paid Customer reservation to a verified Keycloak organization and owner.</summary>
public interface IOrganizationProvisioner
{
    /// <summary>Ensures the organization exists and its reserved owner is a member.</summary>
    /// <param name="tenantId">The Customer-generated reservation GUID.</param>
    /// <param name="identifier">The Customer-owned canonical tenant slug.</param>
    /// <param name="ownerSubjectId">The Keycloak subject recorded at reservation.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The generated Keycloak organization identifier.</returns>
    Task<Guid> EnsureOwnerOrganizationAsync(Guid tenantId, string identifier, string ownerSubjectId, CancellationToken cancellationToken);
}

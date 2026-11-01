namespace Customers.Application.Tenants;

/// <summary>Authenticated Customer command for one independent operator's tenant purge attestation.</summary>
/// <param name="TenantId">Canonical global tenant registry UUID from the route.</param>
/// <param name="OperatorSubjectId">Verified Keycloak subject of a platform manager.</param>
public sealed record ApproveTenantPurgeCommand(Guid TenantId, string OperatorSubjectId);

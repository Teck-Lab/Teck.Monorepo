namespace Teck.LocalIdentity;

/// <summary>Represents one Keycloak organization after its generated identifier has been read back.</summary>
/// <param name="Id">The generated Keycloak organization identifier.</param>
/// <param name="Alias">The local tenant identifier alias.</param>
/// <param name="TenantIdentifier">The tenant registry identifier declared for the organization.</param>
/// <param name="DatabaseStrategy">The tenant database strategy.</param>
/// <param name="DatabaseProvider">The tenant database provider.</param>
/// <param name="HasReadReplicas">Whether the tenant has read replicas.</param>
public sealed record ProvisionedOrganization(Guid Id, string Alias, string TenantIdentifier, string DatabaseStrategy, string DatabaseProvider, bool HasReadReplicas);

namespace Gateway.Public.Edge;

/// <summary>Header and claim names used for tenant resolution at the edge.</summary>
/// <param name="TenantIdHeaderName">The trusted tenant id header.</param>
/// <param name="OrganizationClaimName">The organization claim name.</param>
/// <param name="TenantIdClaimName">The tenant id claim name.</param>
public sealed record EdgeTenantOptions(string TenantIdHeaderName, string OrganizationClaimName, string TenantIdClaimName);

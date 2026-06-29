namespace Gateway.Public.Edge;

/// <summary>Extension methods that bind <see cref="EdgeTenantOptions"/> from configuration.</summary>
public static class EdgeTenantOptionsExtensions
{
    /// <summary>Reads the edge tenant options from the <c>MultiTenancy</c> configuration section.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound options, using defaults when keys are absent.</returns>
    public static EdgeTenantOptions GetEdgeTenantOptions(this IConfiguration configuration) => new(
        configuration["MultiTenancy:TenantIdHeaderName"] ?? "X-TenantId",
        configuration["MultiTenancy:OrganizationClaimName"] ?? "organization",
        configuration["MultiTenancy:TenantIdClaimName"] ?? "tenant_id");
}

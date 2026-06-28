namespace Gateway.Public.Edge;

/// <summary>Resolves a tenant's database strategy for downstream routing.</summary>
public interface ITenantDatabaseStrategyResolver
{
    /// <summary>Resolves the database strategy for a tenant and service.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="serviceName">The downstream service name (cluster id).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The lookup result.</returns>
    Task<TenantDbStrategyResult> ResolveAsync(string tenantId, string? serviceName, CancellationToken ct);
}

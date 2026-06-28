namespace Gateway.Public.Edge;

/// <summary>Trusted internal header and HttpContext item keys used by the edge pipeline.</summary>
public static class EdgeHeaders
{
    /// <summary>Header carrying the resolved tenant database strategy to downstream services.</summary>
    public const string TenantDbStrategy = "X-Tenant-DbStrategy";

    /// <summary>HttpContext.Items key for the exchanged downstream access token.</summary>
    public const string ExchangedTokenItemKey = "Edge:ExchangedAccessToken";

    /// <summary>HttpContext.Items key for the resolved tenant id.</summary>
    public const string ResolvedTenantIdItemKey = "Edge:ResolvedTenantId";
}

namespace Gateway.Public.Edge;

/// <summary>How a route is authenticated and tenant-scoped at the edge.</summary>
public enum EdgeAccessMode
{
    /// <summary>No authentication; tenant resolved from header if present.</summary>
    Anonymous,

    /// <summary>Public route that still requires a tenant, resolved from the header.</summary>
    TenantFromHeader,

    /// <summary>Authenticated route; tenant resolved from token claims.</summary>
    Authenticated,
}

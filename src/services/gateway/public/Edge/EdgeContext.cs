namespace Gateway.Public.Edge;

/// <summary>Mutable per-request edge state passed between pipeline steps.</summary>
public sealed class EdgeContext
{
    /// <summary>Initializes a new instance of the <see cref="EdgeContext"/> class.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="policy">The resolved route policy.</param>
    public EdgeContext(HttpContext httpContext, EdgeAccessPolicy policy)
    {
        HttpContext = httpContext;
        Policy = policy;
    }

    /// <summary>Gets the current HTTP context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>Gets the resolved route policy.</summary>
    public EdgeAccessPolicy Policy { get; }

    /// <summary>Gets or sets the resolved tenant id.</summary>
    public string? ResolvedTenantId { get; set; }

    /// <summary>Gets or sets the resolved tenant database strategy.</summary>
    public string? DbStrategy { get; set; }

    /// <summary>Gets or sets the exchanged downstream access token.</summary>
    public string? ExchangedToken { get; set; }

    /// <summary>Gets or sets the YARP cluster id for the matched route, used as the service name in tenant lookups.</summary>
    public string? ClusterId { get; set; }
}

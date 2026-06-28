namespace Gateway.Public.Edge;

/// <summary>The outcome of a tenant DB-strategy lookup.</summary>
/// <param name="Success">Whether the lookup succeeded.</param>
/// <param name="DatabaseStrategy">The resolved strategy.</param>
/// <param name="StatusCode">The HTTP status to map on failure.</param>
/// <param name="ErrorCode">The machine error code on failure.</param>
/// <param name="ErrorDetail">The human detail on failure.</param>
public sealed record TenantDbStrategyResult(bool Success, string? DatabaseStrategy, int? StatusCode, string? ErrorCode, string? ErrorDetail)
{
    /// <summary>Creates a successful result.</summary>
    /// <param name="strategy">The strategy.</param>
    /// <returns>A success result.</returns>
    public static TenantDbStrategyResult Ok(string strategy) => new(true, strategy, null, null, null);

    /// <summary>Creates a failure result.</summary>
    /// <param name="status">The HTTP status.</param>
    /// <param name="code">The error code.</param>
    /// <param name="detail">The detail.</param>
    /// <returns>A failure result.</returns>
    public static TenantDbStrategyResult Fail(int status, string code, string detail) => new(false, null, status, code, detail);
}

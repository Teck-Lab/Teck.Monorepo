namespace SharedKernel.Infrastructure.Middlewares;

/// <summary>
/// Options that control the per-tenant token-bucket rate limiting applied by
/// <see cref="TenantRateLimitMiddleware"/>.
/// </summary>
public sealed class TenantRateLimitOptions
{
    /// <summary>
    /// The configuration section name from which these options are bound.
    /// </summary>
    public const string SectionName = "TenantRateLimit";

    /// <summary>
    /// Gets the name of the HTTP header that carries the tenant identifier (default: "X-TenantId").
    /// </summary>
    public string HeaderName { get; init; } = "X-TenantId";

    /// <summary>
    /// Gets the tenant identifier used when no tenant header is present (default: "anonymous").
    /// </summary>
    public string MissingTenantId { get; init; } = "anonymous";

    /// <summary>
    /// Gets the request path prefixes that bypass rate limiting (default: "/health" and "/alive").
    /// </summary>
    public string[] SkipPaths { get; init; } = ["/health", "/alive"];

    /// <summary>
    /// Gets the maximum number of tokens the bucket can hold (default: 100).
    /// </summary>
    public int TokenLimit { get; init; } = 100;

    /// <summary>
    /// Gets the number of tokens added to the bucket each replenishment period (default: 1).
    /// </summary>
    public int TokensPerPeriod { get; init; } = 1;

    /// <summary>
    /// Gets the interval at which tokens are replenished (default: 1 second).
    /// </summary>
    public TimeSpan ReplenishmentPeriod { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the maximum number of queued requests permitted when the bucket is empty (default: 0).
    /// </summary>
    public int QueueLimit { get; init; } = 0;

    /// <summary>
    /// Gets a value indicating whether tokens are replenished automatically (default: true).
    /// </summary>
    public bool AutoReplenishment { get; init; } = true;
}

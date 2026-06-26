using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace SharedKernel.Infrastructure.Middlewares;

/// <summary>
/// ASP.NET Core middleware that applies a per-tenant token-bucket rate limit to incoming requests,
/// responding with HTTP 429 when a tenant exceeds its configured allowance.
/// </summary>
/// <param name="next">The next middleware in the request pipeline.</param>
/// <param name="options">The configured per-tenant rate limit options.</param>
/// <param name="logger">The logger used to record rate limiting activity.</param>
public sealed class TenantRateLimitMiddleware(
    RequestDelegate next,
    IOptions<TenantRateLimitOptions> options,
    ILogger<TenantRateLimitMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly TenantRateLimitOptions _options = options.Value;
    private readonly ILogger<TenantRateLimitMiddleware> _logger = logger;
    private readonly ConcurrentDictionary<string, RateLimiter> _limiters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Invokes the middleware, acquiring a rate-limit lease for the resolved tenant and either continuing
    /// the pipeline or returning an HTTP 429 response when the limit has been exceeded.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        string tenantId = ResolveTenantId(context.Request.Headers);
        RateLimiter limiter = _limiters.GetOrAdd(tenantId, _ => CreateLimiter());

        using RateLimitLease lease = await limiter.AcquireAsync(1, context.RequestAborted).ConfigureAwait(false);

        if (lease.IsAcquired)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        TimeSpan retryAfter = _options.ReplenishmentPeriod;

        if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan suggestedRetryAfter) && suggestedRetryAfter > TimeSpan.Zero)
        {
            retryAfter = suggestedRetryAfter;
        }

        _logger.LogWarning("Tenant {TenantId} exceeded the rate limit.", tenantId);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("Too Many Requests", context.RequestAborted).ConfigureAwait(false);
    }

    private RateLimiter CreateLimiter()
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = _options.TokenLimit,
            TokensPerPeriod = _options.TokensPerPeriod,
            ReplenishmentPeriod = _options.ReplenishmentPeriod,
            QueueLimit = _options.QueueLimit,
            AutoReplenishment = _options.AutoReplenishment,
        });
    }

    private string ResolveTenantId(IHeaderDictionary headers)
    {
        if (headers.TryGetValue(_options.HeaderName, out StringValues tenantValues) && !StringValues.IsNullOrEmpty(tenantValues))
        {
            return tenantValues.ToString();
        }

        return _options.MissingTenantId;
    }

    private bool ShouldSkip(PathString path)
    {
        return _options.SkipPaths.Any(skipPath => path.StartsWithSegments(skipPath));
    }
}

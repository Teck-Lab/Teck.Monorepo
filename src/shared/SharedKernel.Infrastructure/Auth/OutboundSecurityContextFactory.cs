using Microsoft.AspNetCore.Http;

namespace SharedKernel.Infrastructure.Auth;

/// <summary>
/// Creates outbound security context data for downstream service calls.
/// </summary>
public interface IOutboundSecurityContextFactory
{
    /// <summary>
    /// Creates an outbound security context for a target audience.
    /// </summary>
    /// <param name="httpContext">The current HTTP context, if available.</param>
    /// <param name="audience">The destination audience/client identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The outbound security context.</returns>
    Task<OutboundSecurityContext> CreateAsync(
        HttpContext? httpContext,
        string audience,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents outbound token and tenant metadata propagated to downstream services.
/// </summary>
/// <param name="AccessToken">The exchanged outbound access token.</param>
/// <param name="TenantId">The resolved tenant identifier header value.</param>
/// <param name="TenantDbStrategy">The resolved tenant database strategy header value.</param>
public sealed record OutboundSecurityContext(
    string? AccessToken,
    string? TenantId,
    string? TenantDbStrategy);

/// <summary>
/// Builds outbound security context values from the current <see cref="HttpContext"/>.
/// </summary>
public sealed class HttpContextOutboundSecurityContextFactory : IOutboundSecurityContextFactory
{
    private const string TenantIdHeader = "X-TenantId";
    private const string TenantDbStrategyHeader = "X-Tenant-DbStrategy";

    private readonly IServiceTokenExchangeService _tokenExchangeService;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContextOutboundSecurityContextFactory"/> class.
    /// </summary>
    /// <param name="tokenExchangeService">Service used to exchange inbound token for outbound token.</param>
    public HttpContextOutboundSecurityContextFactory(IServiceTokenExchangeService tokenExchangeService)
    {
        _tokenExchangeService = tokenExchangeService;
    }

    /// <summary>
    /// Creates outbound security context from HTTP headers and exchanged token.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="audience">Target audience/client identifier for token exchange.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The outbound security context.</returns>
    public async Task<OutboundSecurityContext> CreateAsync(
        HttpContext? httpContext,
        string audience,
        CancellationToken cancellationToken = default)
    {
        if (httpContext is null)
        {
            return new OutboundSecurityContext(
                AccessToken: null,
                TenantId: null,
                TenantDbStrategy: null);
        }

        string? subjectToken = ExtractBearerToken(httpContext.Request.Headers.Authorization.ToString());
        string? tenantId = ReadHeader(httpContext, TenantIdHeader);
        string? tenantDbStrategy = ReadHeader(httpContext, TenantDbStrategyHeader);

        string? exchangedAccessToken = null;
        if (!string.IsNullOrWhiteSpace(subjectToken))
        {
            ServiceTokenResult exchanged = await _tokenExchangeService.ExchangeTokenAsync(
                subjectToken,
                audience,
                contextKey: tenantId ?? string.Empty,
                cancellationToken);

            exchangedAccessToken = exchanged.AccessToken;
        }

        return new OutboundSecurityContext(
            AccessToken: exchangedAccessToken,
            TenantId: tenantId,
            TenantDbStrategy: tenantDbStrategy);
    }

    private static string? ReadHeader(HttpContext httpContext, string headerName)
    {
        string value = httpContext.Request.Headers[headerName].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authorizationHeader.Substring("Bearer ".Length).Trim();
    }
}

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

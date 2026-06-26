namespace SharedKernel.Infrastructure.Auth;

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

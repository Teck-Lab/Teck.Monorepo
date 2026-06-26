namespace SharedKernel.Infrastructure.Auth;

/// <summary>
/// Exchanges inbound user tokens for service-specific access tokens.
/// </summary>
public interface IServiceTokenExchangeService
{
    /// <summary>
    /// Exchanges the provided subject token for an audience-specific access token.
    /// </summary>
    /// <param name="subjectToken">The incoming bearer token.</param>
    /// <param name="audience">The target audience/client for the exchanged token.</param>
    /// <param name="contextKey">A context discriminator used in cache key composition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exchanged token and expiration metadata.</returns>
    Task<ServiceTokenResult> ExchangeTokenAsync(
        string subjectToken,
        string audience,
        string contextKey,
        CancellationToken cancellationToken = default);
}

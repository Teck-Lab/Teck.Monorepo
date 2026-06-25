namespace SharedKernel.Infrastructure.Auth;

/// <summary>
/// Represents the exchanged service token and its expiration time.
/// </summary>
/// <param name="AccessToken">The exchanged access token.</param>
/// <param name="ExpiresAt">UTC timestamp when the token expires.</param>
public sealed record ServiceTokenResult(string AccessToken, DateTime ExpiresAt);

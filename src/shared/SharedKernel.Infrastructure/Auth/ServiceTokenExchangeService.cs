using System.Security.Cryptography;
using System.Text;
using IdentityModel.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ZiggyCreatures.Caching.Fusion;

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

/// <summary>
/// Represents the exchanged service token and its expiration time.
/// </summary>
/// <param name="AccessToken">The exchanged access token.</param>
/// <param name="ExpiresAt">UTC timestamp when the token expires.</param>
public sealed record ServiceTokenResult(string AccessToken, DateTime ExpiresAt);

/// <summary>
/// Represents a token exchange failure with an optional mapped HTTP status.
/// </summary>
public sealed class TokenExchangeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TokenExchangeException"/> class.
    /// </summary>
    public TokenExchangeException()
        : base("Token exchange failed.")
    {
        Error = "unknown_error";
        Description = "n/a";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenExchangeException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public TokenExchangeException(string message)
        : base(message)
    {
        Error = "unknown_error";
        Description = "n/a";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenExchangeException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">Inner exception.</param>
    public TokenExchangeException(string message, Exception innerException)
        : base(message, innerException)
    {
        Error = "unknown_error";
        Description = "n/a";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenExchangeException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="error">Identity provider error code.</param>
    /// <param name="description">Identity provider error description.</param>
    /// <param name="statusCode">Mapped HTTP status code.</param>
    /// <param name="isAuthFailure">Whether the failure is authorization/authentication related.</param>
    public TokenExchangeException(
        string message,
        string error,
        string description,
        int statusCode,
        bool isAuthFailure)
        : base(message)
    {
        Error = error;
        Description = description;
        StatusCode = statusCode;
        IsAuthFailure = isAuthFailure;
    }

    /// <summary>
    /// Gets the identity provider error code.
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// Gets the identity provider error description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the mapped HTTP status code for the failure.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets a value indicating whether the error represents an auth failure.
    /// </summary>
    public bool IsAuthFailure { get; }
}

/// <summary>
/// Default token exchange implementation backed by Keycloak and FusionCache.
/// </summary>
public sealed class ServiceTokenExchangeService : IServiceTokenExchangeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFusionCache _fusionCache;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceTokenExchangeService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory for HTTP clients.</param>
    /// <param name="fusionCache">FusionCache instance for token caching.</param>
    /// <param name="configuration">Application configuration root.</param>
    public ServiceTokenExchangeService(
        IHttpClientFactory httpClientFactory,
        IFusionCache fusionCache,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _fusionCache = fusionCache;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<ServiceTokenResult> ExchangeTokenAsync(
        string subjectToken,
        string audience,
        string contextKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectToken))
        {
            throw new ArgumentNullException(nameof(subjectToken));
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new ArgumentNullException(nameof(audience));
        }

        string cacheKey = BuildCacheKey(subjectToken, audience, contextKey);

        HttpClient client = _httpClientFactory.CreateClient("KeycloakTokenClient");
        string tokenEndpoint = ResolveTokenEndpoint(_configuration);

        using var tokenExchangeRequest = new TokenExchangeTokenRequest
        {
            Address = tokenEndpoint,
            ClientId = ResolveClientId(_configuration),
            ClientSecret = ResolveClientSecret(_configuration),
            SubjectToken = subjectToken,
            SubjectTokenType = "urn:ietf:params:oauth:token-type:access_token",
            Audience = audience,
        };

        var response = await client.RequestTokenExchangeTokenAsync(
            tokenExchangeRequest,
            cancellationToken);

        if (response.IsError)
        {
            await _fusionCache.RemoveAsync(cacheKey, token: cancellationToken);

            int statusCode = (int)response.HttpStatusCode;
            string description = string.IsNullOrWhiteSpace(response.ErrorDescription)
                ? "n/a"
                : response.ErrorDescription;

            string error = string.IsNullOrWhiteSpace(response.Error) ? "unknown_error" : response.Error;

            if (TryMapAuthFailure(error, description, out int mappedStatusCode))
            {
                throw new TokenExchangeException(
                    $"Token exchange denied: {error}; status={mappedStatusCode}; description={description}",
                    error,
                    description,
                    mappedStatusCode,
                    isAuthFailure: true);
            }

            throw new HttpRequestException($"Token exchange failed: {error}; status={statusCode}; description={description}");
        }

        if (string.IsNullOrWhiteSpace(response.AccessToken))
        {
            throw new HttpRequestException("Token exchange failed: access_token is missing");
        }

        if (response.ExpiresIn <= 0)
        {
            throw new HttpRequestException("Token exchange failed: expires_in is missing or invalid");
        }

        DateTime expiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn);
        var serviceTokenResult = new ServiceTokenResult(response.AccessToken!, expiresAt);

        return serviceTokenResult;
    }

    private static string BuildCacheKey(string subjectToken, string audience, string contextKey)
    {
        string safeContext = string.IsNullOrWhiteSpace(contextKey) ? "global" : contextKey;
        string tokenHash = Sha256(subjectToken);

        return $"service-token:v2:{tokenHash}:{audience}:{safeContext}";
    }

    private static string ResolveTokenEndpoint(IConfiguration configuration)
    {
        string? explicitEndpoint = configuration["Keycloak:TokenEndpoint"];
        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            return explicitEndpoint;
        }

        string? authServerUrl = configuration["Keycloak:auth-server-url"];
        string? realm = configuration["Keycloak:realm"];
        if (string.IsNullOrWhiteSpace(authServerUrl) || string.IsNullOrWhiteSpace(realm))
        {
            throw new InvalidOperationException("Keycloak token exchange is not configured. Set Keycloak:TokenEndpoint or Keycloak auth-server-url + realm.");
        }

        return $"{authServerUrl.TrimEnd('/')}/realms/{realm.Trim('/')}/protocol/openid-connect/token";
    }

    private static string ResolveClientId(IConfiguration configuration)
    {
        string? clientId = configuration["Keycloak:resource"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Keycloak token exchange is not configured. Set Keycloak:resource.");
        }

        return clientId;
    }

    private static string ResolveClientSecret(IConfiguration configuration)
    {
        string? clientSecret = configuration["Keycloak:credentials:secret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Keycloak token exchange is not configured. Set Keycloak:credentials:secret.");
        }

        return clientSecret;
    }

    private static string Sha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(bytes);
    }

    private static bool TryMapAuthFailure(string error, string description, out int statusCode)
    {
        statusCode = 0;

        if (string.Equals(error, "invalid_token", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error, "invalid_grant", StringComparison.OrdinalIgnoreCase))
        {
            statusCode = StatusCodes.Status401Unauthorized;
            return true;
        }

        if (string.Equals(error, "invalid_request", StringComparison.OrdinalIgnoreCase) &&
            (description.Contains("invalid token", StringComparison.OrdinalIgnoreCase) ||
             description.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
             description.Contains("subject token", StringComparison.OrdinalIgnoreCase)))
        {
            statusCode = StatusCodes.Status401Unauthorized;
            return true;
        }

        if (string.Equals(error, "unauthorized_client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error, "insufficient_scope", StringComparison.OrdinalIgnoreCase))
        {
            statusCode = StatusCodes.Status403Forbidden;
            return true;
        }

        if (string.Equals(error, "invalid_client", StringComparison.OrdinalIgnoreCase))
        {
            statusCode = StatusCodes.Status401Unauthorized;
            return true;
        }

        return false;
    }
}

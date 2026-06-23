// <copyright file="ClientCredentialsAuthorizationServerClient.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Keycloak.AuthServices.Authorization.AuthorizationServer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SharedKernel.Infrastructure.Auth;

/// <summary>
/// Replaces the default <see cref="AuthorizationServerClient"/> to correctly authenticate the
/// resource server and identify the requesting party on UMA ticket requests.
///
/// <para>
/// The default Keycloak.AuthServices implementation forwards the current request's bearer token
/// as <c>Authorization: Bearer</c> on the UMA call but provides no resource-server authentication.
/// When Standard V2 token exchange is used, that bearer token has <c>azp=teck-public-gateway</c>.
/// Keycloak interprets the <c>Authorization: Bearer</c> as the resource-server PAT, rejects it
/// because the token belongs to a different client, and returns
/// <c>unauthorized_client / Invalid identity</c>.
/// </para>
///
/// <para>
/// The correct UMA 2.0 split is:
/// <list type="bullet">
///   <item><b>Resource-server auth</b>: <c>Authorization: Basic base64(client_id:client_secret)</c></item>
///   <item><b>Requesting-party identity</b>: <c>subject_token=&lt;exchanged_user_token&gt;</c> in the form body</item>
/// </list>
/// Keycloak then extracts the user <c>sub</c> from <c>subject_token</c> and evaluates UMA policies.
/// With <c>fetchRoles: true</c> in the policy config, roles are looked up from the DB rather than
/// read from the token claims, so the empty <c>resource_access.teck-product</c> in the exchanged
/// token is irrelevant.
/// </para>
/// </summary>
internal sealed class ClientCredentialsAuthorizationServerClient : IAuthorizationServerClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKeycloakAccessTokenProvider _accessTokenProvider;
    private readonly IOptions<KeycloakAuthorizationServerOptions> _options;
    private readonly ILogger<ClientCredentialsAuthorizationServerClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientCredentialsAuthorizationServerClient"/> class.
    /// </summary>
    public ClientCredentialsAuthorizationServerClient(
        IHttpClientFactory httpClientFactory,
        IKeycloakAccessTokenProvider accessTokenProvider,
        IOptions<KeycloakAuthorizationServerOptions> options,
        ILogger<ClientCredentialsAuthorizationServerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _accessTokenProvider = accessTokenProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> VerifyAccessToResource(
        string resource,
        string scope,
        CancellationToken cancellationToken = default) =>
        VerifyInternalAsync(resource, scope, userToken: null, cancellationToken);

    /// <inheritdoc />
    public Task<bool> VerifyAccessToResource(
        string resource,
        string scope,
        ScopesValidationMode? scopesValidationMode = null,
        CancellationToken cancellationToken = default) =>
        VerifyInternalAsync(resource, scope, userToken: null, cancellationToken);

    /// <inheritdoc />
    public Task<bool> VerifyAccessToResource(
        string resource,
        string scope,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        VerifyInternalAsync(resource, scope, userToken: accessToken, cancellationToken);

    /// <inheritdoc />
    public Task<bool> VerifyAccessToResource(
        string resource,
        string scope,
        string accessToken,
        ScopesValidationMode? scopesValidationMode = null,
        CancellationToken cancellationToken = default) =>
        VerifyInternalAsync(resource, scope, userToken: accessToken, cancellationToken);

    private async Task<bool> VerifyInternalAsync(
        string resource,
        string scope,
        string? userToken,
        CancellationToken cancellationToken)
    {
        KeycloakAuthorizationServerOptions opts = _options.Value;

        string tokenEndpoint = opts.KeycloakTokenEndpoint
            ?? throw new InvalidOperationException(
                "KeycloakAuthorizationServerOptions.KeycloakTokenEndpoint is not configured.");

        string clientId = opts.Resource
            ?? throw new InvalidOperationException(
                "KeycloakAuthorizationServerOptions.Resource (client_id) is not configured.");

        string clientSecret = opts.Credentials?.Secret
            ?? throw new InvalidOperationException(
                "KeycloakAuthorizationServerOptions.Credentials.Secret is not configured.");

        // Use the explicitly supplied token or fall back to the current request's saved token.
        if (string.IsNullOrWhiteSpace(userToken))
        {
            userToken = await _accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(userToken))
        {
            _logger.LogWarning(
                "[ClientCredentialsAuthorizationServerClient] No requesting-party token available for UMA check on '{Resource}#{Scope}'.",
                resource,
                scope);

            return false;
        }

        // UMA 2.0 ticket request:
        //   Authorization: Basic base64(client_id:client_secret)  â€” resource-server authentication
        //   subject_token=<exchanged_user_token>                   â€” requesting-party identity
        //
        // This is distinct from sending Authorization: Bearer which Keycloak would treat as
        // the resource-server PAT and reject when azp != the resource client.
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:uma-ticket",
            ["audience"] = clientId,
            ["permission"] = $"{resource}#{scope}",
            ["response_mode"] = "decision",
            ["subject_token"] = userToken,
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
        };

        using HttpClient client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(formData),
        };

        // Authenticate the resource server with HTTP Basic (RFC 7617).
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "[ClientCredentialsAuthorizationServerClient] Verifying access to resource '{Resource}' with scope '{Scope}'",
                resource,
                scope);
        }

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning(
                "[ClientCredentialsAuthorizationServerClient] UMA ticket denied: Resource={Resource}, Scope={Scope}, Status={Status}, Body={Body}",
                resource,
                scope,
                (int)response.StatusCode,
                errorBody);

            return false;
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        using JsonDocument jsonDocument = JsonDocument.Parse(content);
        bool result = jsonDocument.RootElement.TryGetProperty("result", out JsonElement resultElement)
            && resultElement.GetBoolean();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "[ClientCredentialsAuthorizationServerClient] UMA decision for '{Resource}#{Scope}': {Result}",
                resource,
                scope,
                result);
        }

        return result;
    }
}

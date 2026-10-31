using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Credentials for the dedicated tenant-database-worker service account in Billing.</summary>
public sealed class BillingPurgeFenceClientOptions
{
    /// <summary>Gets or sets the Keycloak token endpoint.</summary>
    public string TokenEndpoint { get; set; } = string.Empty;
    /// <summary>Gets or sets the dedicated tenant-database-worker client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;
    /// <summary>Gets or sets the dedicated tenant-database-worker client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>Gets or sets the required Billing purge-fence scope.</summary>
    public string Scope { get; set; } = string.Empty;
    /// <summary>Gets or sets the internal Billing service base URI.</summary>
    public string BillingBaseUrl { get; set; } = string.Empty;
}

/// <summary>Obtains and completes Billing's durable fence using only the dedicated worker identity.</summary>
public sealed class BillingPurgeFenceClient(HttpClient httpClient, IOptions<BillingPurgeFenceClientOptions> options)
    : IBillingPurgeFenceClient
{
    private readonly BillingPurgeFenceClientOptions configuration = options.Value;

    /// <inheritdoc />
    public async Task<Guid?> TryAcquireAsync(Guid tenantId, Guid subscriptionId, Guid sourceOrderId, Guid approvalId,
        CancellationToken ct)
    {
        if (!HasConfiguration() || tenantId == Guid.Empty || subscriptionId == Guid.Empty ||
            sourceOrderId == Guid.Empty || approvalId == Guid.Empty ||
            !Uri.TryCreate(configuration.BillingBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        string? token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)) return null;

        var requestUri = new Uri(baseUri, $"internal/purge-fences/{tenantId:D}/acquire");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new { subscriptionId, sourceOrderId, approvalId }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) return null;

        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        if (!body.RootElement.TryGetProperty("grantId", out JsonElement grant) ||
            !Guid.TryParse(grant.GetString(), out Guid grantId) || grantId == Guid.Empty)
        {
            return null;
        }

        return grantId;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(Guid grantId, CancellationToken ct)
    {
        if (!HasConfiguration() || grantId == Guid.Empty ||
            !Uri.TryCreate(configuration.BillingBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        string? token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)) return false;

        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(baseUri, $"internal/purge-fences/{grantId:D}/complete"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NoContent;
    }

    private bool HasConfiguration() =>
        !string.IsNullOrWhiteSpace(configuration.TokenEndpoint) &&
        !string.IsNullOrWhiteSpace(configuration.ClientId) &&
        !string.IsNullOrWhiteSpace(configuration.ClientSecret) &&
        !string.IsNullOrWhiteSpace(configuration.Scope) &&
        Uri.TryCreate(configuration.TokenEndpoint, UriKind.Absolute, out _);

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(configuration.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = configuration.ClientId,
                ["client_secret"] = configuration.ClientSecret,
                ["scope"] = configuration.Scope,
            }), ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) return null;

        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        return body.RootElement.TryGetProperty("access_token", out JsonElement token)
            ? token.GetString()
            : null;
    }
}

using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Billings.Host.Infrastructure;

/// <summary>Customer reservation client authenticated as Billing using Keycloak client credentials.</summary>
/// <param name="httpClient">The HTTP client used for OAuth and Customer requests.</param>
/// <param name="options">The Billing service-client configuration.</param>
public sealed class CustomerReservationClient(HttpClient httpClient, IOptions<CustomerReservationOptions> options)
{
    /// <summary>Gets a canonical reserved tenant, or null when Customer has no matching reservation.</summary>
    /// <param name="reservationId">The Customer-issued reservation identifier.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The canonical Customer reservation, or null when none exists.</returns>
    public async Task<PurchaseReservationResponse?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var config = options.Value;
        using var tokenResponse = await httpClient.PostAsync(config.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret, ["scope"] = config.Scope,
            }), cancellationToken).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        var token = tokenJson.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Keycloak returned an empty Billing service token.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(config.CustomerBaseUrl), $"internal/purchase-reservations/{reservationId:D}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PurchaseReservationResponse>(cancellationToken).ConfigureAwait(false);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Teck.LocalIdentity;

internal sealed class KeycloakAdminClient(HttpClient httpClient, LocalIdentityOptions options)
{
    private string? _accessToken;

    internal async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "realms/master/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", "admin-cli"),
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("username", options.AdminUsername),
                new KeyValuePair<string, string>("password", options.AdminPassword),
            ]),
        };
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        _accessToken = document.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            throw new InvalidOperationException("Keycloak did not return an administration access token.");
        }
    }

    internal async Task<JsonElement?> GetOptionalAsync(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<JsonElement>> GetArrayAsync(string path, CancellationToken cancellationToken)
    {
        JsonElement? response = await GetOptionalAsync(path, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return [];
        }

        if (response.Value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Keycloak returned a non-array response for '{path}'.");
        }

        return response.Value.EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    internal Task PostAsync(string path, JsonElement value, CancellationToken cancellationToken) =>
        SendJsonAsync(HttpMethod.Post, path, value, cancellationToken);

    internal Task PutAsync(string path, JsonElement value, CancellationToken cancellationToken) =>
        SendJsonAsync(HttpMethod.Put, path, value, cancellationToken);

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        }

        return request;
    }

    private async Task SendJsonAsync(HttpMethod method, string path, JsonElement value, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        request.Content = JsonContent.Create(value);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException($"Keycloak administration request failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {content}", null, response.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Customers.Application.EventHandlers.IntegrationEvents;
using Microsoft.Extensions.Options;

namespace Customers.Host.Infrastructure;

/// <summary>Links a paid Customer reservation and its owner to a Keycloak organization.</summary>
/// <param name="client">Keycloak Admin REST client.</param>
/// <param name="options">The narrowly privileged service-account configuration.</param>
public sealed class KeycloakOrganizationProvisioner(HttpClient client, IOptions<KeycloakOrganizationOptions> options)
    : IOrganizationProvisioner
{
    /// <inheritdoc/>
    public async Task<Guid> EnsureOwnerOrganizationAsync(Guid tenantId, string identifier, string ownerSubjectId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(ownerSubjectId))
        {
            throw new ArgumentException("A reserved tenant and its Keycloak owner are required.");
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(settings.AdminClientId) ||
            string.IsNullOrWhiteSpace(settings.AdminClientSecret))
        {
            throw new InvalidOperationException("Keycloak organization administration is not configured.");
        }

        var root = settings.ServerUrl.TrimEnd('/');
        var collection = $"{root}/admin/realms/{Uri.EscapeDataString(settings.Realm)}/organizations";
        using var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = settings.AdminClientId,
            ["client_secret"] = settings.AdminClientSecret,
        });
        using var tokenResponse = await client.PostAsync(
            $"{root}/realms/{Uri.EscapeDataString(settings.Realm)}/protocol/openid-connect/token",
            tokenContent, cancellationToken).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var token = tokenJson.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Keycloak returned no organization administration token.");
        }

        // The reservation owner is a signed Keycloak subject, not a mutable username supplied by the caller.
        using (var userResponse = await SendAsync(HttpMethod.Get,
            $"{root}/admin/realms/{Uri.EscapeDataString(settings.Realm)}/users/{Uri.EscapeDataString(ownerSubjectId)}",
            token, null, cancellationToken).ConfigureAwait(false))
        {
            userResponse.EnsureSuccessStatusCode();
            using var user = await JsonDocument.ParseAsync(
                await userResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!string.Equals(user.RootElement.GetProperty("id").GetString(), ownerSubjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The reserved Keycloak owner could not be verified.");
            }
        }

        var organizationId = await FindOrganizationAsync().ConfigureAwait(false);
        if (organizationId is null)
        {
            var definition = new
            {
                name = identifier,
                alias = identifier,
                enabled = true,
                attributes = new Dictionary<string, string[]> { ["customerTenantId"] = [tenantId.ToString("D")] },
            };
            using var created = await SendAsync(HttpMethod.Post, collection, token, definition, cancellationToken).ConfigureAwait(false);
            if (created.StatusCode != HttpStatusCode.Conflict && !created.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Keycloak organization create failed ({(int)created.StatusCode}): " +
                    await created.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }

            // Read back the owner binding rather than trusting Location or adopting a conflicting alias.
            organizationId = await FindOrganizationAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The created Keycloak organization was not readable.");
        }

        var membersUrl = $"{collection}/{organizationId:D}/members";
        using (var response = await SendAsync(HttpMethod.Get, $"{membersUrl}?max=1000", token, null,
            cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            using var members = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (members.RootElement.EnumerateArray().Any(member =>
                string.Equals(member.GetProperty("id").GetString(), ownerSubjectId, StringComparison.Ordinal)))
            {
                return organizationId.Value;
            }
        }

        using var linked = await SendAsync(HttpMethod.Post, membersUrl, token, ownerSubjectId,
            cancellationToken).ConfigureAwait(false);
        if (linked.StatusCode != HttpStatusCode.Conflict)
        {
            linked.EnsureSuccessStatusCode();
        }

        return organizationId.Value;

        async Task<Guid?> FindOrganizationAsync()
        {
            using var response = await SendAsync(HttpMethod.Get,
                $"{collection}?search={Uri.EscapeDataString(identifier)}&exact=true", token, null,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var found = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var organization in found.RootElement.EnumerateArray())
            {
                if (!string.Equals(organization.GetProperty("alias").GetString(), identifier, StringComparison.Ordinal))
                {
                    continue;
                }

                // Search results omit attributes; retrieve the complete representation before adoption.
                var id = Guid.Parse(organization.GetProperty("id").GetString()!);
                using var detailsResponse = await SendAsync(HttpMethod.Get, $"{collection}/{id:D}", token, null,
                    cancellationToken).ConfigureAwait(false);
                detailsResponse.EnsureSuccessStatusCode();
                using var details = await JsonDocument.ParseAsync(
                    await detailsResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!details.RootElement.TryGetProperty("attributes", out var attributes) ||
                    !attributes.TryGetProperty("customerTenantId", out var ids) ||
                    !ids.EnumerateArray().Any(value => value.GetString() == tenantId.ToString("D")))
                {
                    throw new InvalidOperationException("Keycloak organization belongs to another reservation.");
                }

                return id;
            }

            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

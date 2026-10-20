// <copyright file="KeycloakTestRealmFixture.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>


using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.Keycloak;
using Xunit;

namespace Teck.Platform.IntegrationTests.Shared;

/// <summary>Owns an isolated Keycloak realm and obtains real user tokens with the password grant.</summary>
public sealed class KeycloakTestRealmFixture : IAsyncLifetime
{
    public const string Realm = "teck";
    public const string TenantId = "tenant-authz-test";
    private const string Image = "quay.io/keycloak/keycloak:26.6.0";
    private const string RealmFile = "test-realms/teck-realm.json";
    private const string AdminPassword = "local-only-test-keycloak-admin-password";
    private KeycloakContainer? container;

    /// <summary>Gets the isolated Keycloak container.</summary>
    public KeycloakContainer Container => container ?? throw new InvalidOperationException("Keycloak is not initialized.");

    /// <summary>Gets the test realm issuer.</summary>
    public string Authority => $"{Container.GetBaseAddress().TrimEnd('/')}/realms/{Realm}";


    /// <summary>Gets the generated organization ID emitted in signed tokens.</summary>
    public string OrganizationId { get; private set; } = string.Empty;

    /// <summary>Gets the generated foreign organization ID emitted in signed tokens.</summary>
    public string ForeignOrganizationId { get; private set; } = string.Empty;
    private async Task ProvisionOrganizationAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(Container.GetBaseAddress(), UriKind.Absolute) };
        string root = FindRepositoryRoot();
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, "tests", "integration", "Teck.Platform.IntegrationTests.Shared", "test-organizations.json")).ConfigureAwait(false));
        foreach (JsonElement organization in manifest.RootElement.GetProperty("organizations").EnumerateArray())
        {
            string alias = organization.GetProperty("alias").GetString()!;
            using var tokenResponse = await client.PostAsync(
                "/realms/master/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "admin-cli",
                    ["username"] = "admin",
                    ["password"] = AdminPassword,
                })).ConfigureAwait(false);
            tokenResponse.EnsureSuccessStatusCode();
            using JsonDocument token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync().ConfigureAwait(false));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());

            var organizationRequest = new
            {
                name = organization.GetProperty("name").GetString(),
                alias,
                enabled = organization.GetProperty("enabled").GetBoolean(),
                domains = organization.GetProperty("domains"),
            };
            using var createResponse = await client.PostAsJsonAsync($"/admin/realms/{Realm}/organizations", organizationRequest).ConfigureAwait(false);
            createResponse.EnsureSuccessStatusCode();
            string location = createResponse.Headers.Location?.ToString() ?? throw new InvalidOperationException("Keycloak did not return the created organization location.");
            string organizationId = Uri.UnescapeDataString(location.TrimEnd('/').Split('/').Last());
            if (alias == "teck-local-alpha") OrganizationId = organizationId;
            else if (alias == "teck-local-foreign") ForeignOrganizationId = organizationId;

            foreach (JsonElement member in organization.GetProperty("members").EnumerateArray())
            {
                string username = member.GetString()!;
                using var usersResponse = await client.GetAsync($"/admin/realms/{Realm}/users?username={Uri.EscapeDataString(username)}&exact=true").ConfigureAwait(false);
                usersResponse.EnsureSuccessStatusCode();
                using JsonDocument users = JsonDocument.Parse(await usersResponse.Content.ReadAsStreamAsync().ConfigureAwait(false));
                string userId = users.RootElement.EnumerateArray().Single().GetProperty("id").GetString()!;
                using var memberResponse = await client.PostAsJsonAsync(
                    $"/admin/realms/{Realm}/organizations/{Uri.EscapeDataString(organizationId)}/members",
                    userId).ConfigureAwait(false);
                memberResponse.EnsureSuccessStatusCode();
            }
        }


    }


    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Teck.Platform.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        string realmPath = Path.Combine(AppContext.BaseDirectory, RealmFile);
        if (!File.Exists(realmPath))
        {
            throw new FileNotFoundException("The isolated Keycloak test realm must be copied to the test output.", realmPath);
        }

        container = new KeycloakBuilder(Image)
            .WithUsername("admin")
            .WithPassword(AdminPassword)
            .WithRealm(realmPath)
            .Build();
        await Container.StartAsync().ConfigureAwait(false);
        await ProvisionOrganizationAsync().ConfigureAwait(false);
    }


    /// <summary>Obtains an actual signed access token for a test user using password grant.</summary>
    public async Task<string> GetUserTokenAsync(
        string username,
        string password,
        string clientId,
        CancellationToken cancellationToken = default,
        string? clientSecret = null,
        string? requestedScope = null)
    {
        using var client = new HttpClient();
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "password"),
            new("client_id", clientId),
            new("username", username),
            new("password", password),
            new("scope", requestedScope is null
                ? "openid organization:* order-api-audience pricing-api-audience public-gateway-audience basket-api-audience customer-api-audience inventory-api-audience billing-api-audience catalog-api-audience"
                : $"openid organization:* {requestedScope}"),
        };
        if (clientSecret is not null)
        {
            form.Add(new KeyValuePair<string, string>("client_secret", clientSecret));
        }

        using var response = await client.PostAsync(
            $"{Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Keycloak token request failed with {(int)response.StatusCode}: {error}");
        }

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync().ConfigureAwait(false);
        }
    }
}

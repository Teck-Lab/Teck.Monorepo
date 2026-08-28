using System.Text.Json;

namespace Teck.LocalIdentity;

/// <summary>Reconciles the committed local Keycloak realm manifest through the administration API.</summary>
public sealed class RealmReconciler
{
    private readonly KeycloakAdminClient _client;

    /// <summary>Initializes a reconciler using the supplied Keycloak HTTP client and configuration.</summary>
    /// <param name="httpClient">The HTTP client used to reach Keycloak.</param>
    /// <param name="options">The local Keycloak administration configuration.</param>
    public RealmReconciler(HttpClient httpClient, LocalIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        httpClient.BaseAddress ??= new Uri(options.BaseUrl, UriKind.Absolute);
        _client = new KeycloakAdminClient(httpClient, options);
    }

    /// <summary>Applies a realm manifest so repeat invocations converge on the same Keycloak state.</summary>
    /// <param name="manifest">The committed Keycloak realm manifest.</param>
    /// <param name="cancellationToken">The token used to cancel API calls.</param>
    /// <returns>A task that completes when the manifest has been reconciled.</returns>
    public async Task ReconcileAsync(JsonDocument manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        JsonElement root = manifest.RootElement;
        string realm = RequiredString(root, "realm");
        string realmPath = $"admin/realms/{Escape(realm)}";

        await _client.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        JsonElement? existingRealm = await _client.GetOptionalAsync(realmPath, cancellationToken).ConfigureAwait(false);
        if (existingRealm is null)
        {
            await _client.PostAsync("admin/realms", root, cancellationToken).ConfigureAwait(false);
            existingRealm = root;
        }

        JsonElement realmSettings = Without(root, "roles", "clients", "users");
        if (!Contains(existingRealm.Value, realmSettings))
        {
            await _client.PutAsync(realmPath, realmSettings, cancellationToken).ConfigureAwait(false);
        }

        foreach (JsonElement role in Array(root, "roles", "realm"))
        {
            await ReconcileNamedAsync($"{realmPath}/roles", role, "name", cancellationToken).ConfigureAwait(false);
        }

        foreach (JsonElement client in Array(root, "clients"))
        {
            await ReconcileClientAsync(realmPath, client, cancellationToken).ConfigureAwait(false);
        }

        foreach (JsonElement user in Array(root, "users"))
        {
            await ReconcileUserAsync(realmPath, user, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileClientAsync(string realmPath, JsonElement client, CancellationToken cancellationToken)
    {
        string clientId = RequiredString(client, "clientId");
        string clientPath = $"{realmPath}/clients";
        IReadOnlyList<JsonElement> clients = await _client.GetArrayAsync($"{clientPath}?clientId={Escape(clientId)}", cancellationToken).ConfigureAwait(false);
        JsonElement clientDefinition = Without(client, "protocolMappers", "authorizationSettings");
        JsonElement? existing = clients.FirstOrDefault(candidate => StringEquals(candidate, "clientId", clientId));
        if (existing is null || existing.Value.ValueKind == JsonValueKind.Undefined)
        {
            await _client.PostAsync(clientPath, clientDefinition, cancellationToken).ConfigureAwait(false);
            clients = await _client.GetArrayAsync($"{clientPath}?clientId={Escape(clientId)}", cancellationToken).ConfigureAwait(false);
            existing = clients.Single(candidate => StringEquals(candidate, "clientId", clientId));
        }
        else if (!Contains(existing.Value, clientDefinition))
        {
            await _client.PutAsync($"{clientPath}/{Escape(RequiredString(existing.Value, "id"))}", clientDefinition, cancellationToken).ConfigureAwait(false);
        }

        string internalId = RequiredString(existing.Value, "id");
        string mapperPath = $"{clientPath}/{Escape(internalId)}/protocol-mappers/models";
        await ReconcileNamedCollectionAsync(mapperPath, Array(client, "protocolMappers"), "name", cancellationToken).ConfigureAwait(false);

        if (client.TryGetProperty("authorizationSettings", out JsonElement authorizationSettings))
        {
            await ReconcileAuthorizationAsync($"{clientPath}/{Escape(internalId)}/authz/resource-server", authorizationSettings, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileAuthorizationAsync(string path, JsonElement authorizationSettings, CancellationToken cancellationToken)
    {
        JsonElement settings = Without(authorizationSettings, "resources", "policies");
        JsonElement? existingSettings = await _client.GetOptionalAsync($"{path}/settings", cancellationToken).ConfigureAwait(false);
        if (existingSettings is null || !Contains(existingSettings.Value, settings))
        {
            await _client.PutAsync(path, settings, cancellationToken).ConfigureAwait(false);
        }

        await ReconcileNamedCollectionAsync($"{path}/resource", Array(authorizationSettings, "resources"), "name", cancellationToken).ConfigureAwait(false);
        await ReconcileNamedCollectionAsync($"{path}/policy", Array(authorizationSettings, "policies"), "name", cancellationToken).ConfigureAwait(false);
    }

    private async Task ReconcileUserAsync(string realmPath, JsonElement user, CancellationToken cancellationToken)
    {
        string username = RequiredString(user, "username");
        string userPath = $"{realmPath}/users";
        IReadOnlyList<JsonElement> users = await _client.GetArrayAsync($"{userPath}?username={Escape(username)}&exact=true", cancellationToken).ConfigureAwait(false);
        JsonElement userDefinition = Without(user, "realmRoles", "credentials");
        JsonElement? existing = users.FirstOrDefault(candidate => StringEquals(candidate, "username", username));
        if (existing is null || existing.Value.ValueKind == JsonValueKind.Undefined)
        {
            await _client.PostAsync(userPath, user, cancellationToken).ConfigureAwait(false);
            users = await _client.GetArrayAsync($"{userPath}?username={Escape(username)}&exact=true", cancellationToken).ConfigureAwait(false);
            existing = users.Single(candidate => StringEquals(candidate, "username", username));
        }
        else if (!Contains(existing.Value, userDefinition))
        {
            await _client.PutAsync($"{userPath}/{Escape(RequiredString(existing.Value, "id"))}", userDefinition, cancellationToken).ConfigureAwait(false);
        }

        string userId = RequiredString(existing.Value, "id");
        if (user.TryGetProperty("realmRoles", out JsonElement roles))
        {
            IReadOnlyList<JsonElement> currentRoles = await _client.GetArrayAsync($"{userPath}/{Escape(userId)}/role-mappings/realm", cancellationToken).ConfigureAwait(false);
            var desiredRoles = new List<JsonElement>();
            foreach (JsonElement roleName in roles.EnumerateArray())
            {
                string name = roleName.GetString() ?? throw new InvalidOperationException("A user realm role must be a string.");
                JsonElement? role = await _client.GetOptionalAsync($"{realmPath}/roles/{Escape(name)}", cancellationToken).ConfigureAwait(false);
                if (role is null)
                {
                    throw new InvalidOperationException($"The manifest assigns unknown realm role '{name}' to '{username}'.");
                }

                desiredRoles.Add(role.Value);
            }

            JsonElement desiredRoleArray = ToArray(desiredRoles);
            if (!ContainsArrayByName(currentRoles, desiredRoles, "name"))
            {
                await _client.PostAsync($"{userPath}/{Escape(userId)}/role-mappings/realm", desiredRoleArray, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReconcileNamedAsync(string collectionPath, JsonElement desired, string nameProperty, CancellationToken cancellationToken)
    {
        string name = RequiredString(desired, nameProperty);
        JsonElement? current = await _client.GetOptionalAsync($"{collectionPath}/{Escape(name)}", cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            await _client.PostAsync(collectionPath, desired, cancellationToken).ConfigureAwait(false);
        }
        else if (!Contains(current.Value, desired))
        {
            await _client.PutAsync($"{collectionPath}/{Escape(name)}", desired, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileNamedCollectionAsync(string collectionPath, IReadOnlyList<JsonElement> desired, string nameProperty, CancellationToken cancellationToken)
    {
        IReadOnlyList<JsonElement> current = await _client.GetArrayAsync(collectionPath, cancellationToken).ConfigureAwait(false);
        foreach (JsonElement item in desired)
        {
            string name = RequiredString(item, nameProperty);
            JsonElement? currentItem = current.FirstOrDefault(candidate => StringEquals(candidate, nameProperty, name));
            if (currentItem is null || currentItem.Value.ValueKind == JsonValueKind.Undefined)
            {
                await _client.PostAsync(collectionPath, item, cancellationToken).ConfigureAwait(false);
            }
            else if (!Contains(currentItem.Value, item))
            {
                string id = currentItem.Value.TryGetProperty("id", out JsonElement value) ? RequiredString(value) : name;
                await _client.PutAsync($"{collectionPath}/{Escape(id)}", item, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private IReadOnlyList<JsonElement> Array(JsonElement element, params string[] path)
    {
        foreach (string segment in path)
        {
            if (!element.TryGetProperty(segment, out element))
            {
                return [];
            }
        }

        return element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(value => value.Clone()).ToArray() : [];
    }

    private bool Contains(JsonElement actual, JsonElement expected)
    {
        if (expected.ValueKind == JsonValueKind.Object)
        {
            return actual.ValueKind == JsonValueKind.Object && expected.EnumerateObject().All(property =>
                actual.TryGetProperty(property.Name, out JsonElement actualValue) && Contains(actualValue, property.Value));
        }

        if (expected.ValueKind == JsonValueKind.Array)
        {
            return actual.ValueKind == JsonValueKind.Array && actual.GetArrayLength() == expected.GetArrayLength() &&
                actual.EnumerateArray().Zip(expected.EnumerateArray()).All(pair => Contains(pair.First, pair.Second));
        }

        return actual.ValueKind == expected.ValueKind && (expected.ValueKind != JsonValueKind.String ||
            string.Equals(actual.GetString(), expected.GetString(), StringComparison.Ordinal)) &&
            (expected.ValueKind == JsonValueKind.String || actual.GetRawText() == expected.GetRawText());
    }

    private bool ContainsArrayByName(IEnumerable<JsonElement> actual, IEnumerable<JsonElement> expected, string nameProperty) =>
        expected.All(item => actual.Any(candidate => StringEquals(candidate, nameProperty, RequiredString(item, nameProperty))));

    private JsonElement Without(JsonElement element, params string[] excluded)
    {
        var excludedNames = excluded.ToHashSet(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(element.EnumerateObject()
            .Where(property => !excludedNames.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property.Value)));
        return document.RootElement.Clone();
    }

    private JsonElement ToArray(IEnumerable<JsonElement> values)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return document.RootElement.Clone();
    }

    private string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) ? RequiredString(value) : throw new InvalidOperationException($"Keycloak manifest property '{property}' is required.");

    private string RequiredString(JsonElement element) => element.GetString() ?? throw new InvalidOperationException("A required Keycloak value was empty.");

    private bool StringEquals(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out JsonElement value) && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private string Escape(string value) => Uri.EscapeDataString(value);
}

using System.Text.Json;

namespace Teck.LocalIdentity;

/// <summary>Reconciles local Keycloak organizations and returns their generated identifiers.</summary>
public sealed class OrganizationReconciler : IOrganizationReconciler
{
    private readonly KeycloakAdminClient client;
    private readonly LocalIdentityOptions options;

    /// <summary>Initializes an organization reconciler using the supplied Keycloak HTTP client and configuration.</summary>
    /// <param name="httpClient">The HTTP client used to reach Keycloak.</param>
    /// <param name="options">The local Keycloak administration configuration.</param>
    public OrganizationReconciler(HttpClient httpClient, LocalIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        httpClient.BaseAddress ??= new Uri(options.BaseUrl, UriKind.Absolute);
        client = new KeycloakAdminClient(httpClient, options);
        this.options = options;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProvisionedOrganization>> ReconcileAsync(JsonDocument manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await client.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

        string collectionPath = $"admin/realms/{Escape(options.Realm)}/organizations";
        var provisioned = new List<ProvisionedOrganization>();
        foreach (JsonElement organization in RequiredArray(manifest.RootElement, "organizations"))
        {
            string alias = RequiredString(organization, "alias");
            JsonElement definition = Without(organization, "members", "tenant");
            string name = RequiredString(definition, "name");
            IReadOnlyList<JsonElement> matches = await client.GetArrayAsync($"{collectionPath}?search={Escape(name)}&exact=true", cancellationToken).ConfigureAwait(false);
            JsonElement? existing = matches.FirstOrDefault(candidate => StringEquals(candidate, "alias", alias));
            if (existing is null || existing.Value.ValueKind == JsonValueKind.Undefined)
            {
                await client.PostAsync(collectionPath, definition, cancellationToken).ConfigureAwait(false);
                matches = await client.GetArrayAsync($"{collectionPath}?search={Escape(name)}&exact=true", cancellationToken).ConfigureAwait(false);
                existing = matches.Single(candidate => StringEquals(candidate, "alias", alias));
            }
            else if (!Contains(existing.Value, definition))
            {
                await client.PutAsync($"{collectionPath}/{Escape(RequiredString(existing.Value, "id"))}", definition, cancellationToken).ConfigureAwait(false);
            }

            Guid organizationId = ParseOrganizationId(RequiredString(existing.Value, "id"), alias);
            await ReconcileMembershipsAsync(collectionPath, organizationId, RequiredArray(organization, "members"), cancellationToken).ConfigureAwait(false);
            provisioned.Add(ToProvisionedOrganization(organization, organizationId));
        }

        return provisioned;
    }

    private async Task ReconcileMembershipsAsync(string collectionPath, Guid organizationId, IReadOnlyList<JsonElement> members, CancellationToken cancellationToken)
    {
        string organizationPath = $"{collectionPath}/{Escape(organizationId.ToString())}";
        IReadOnlyList<JsonElement> existingMembers = await client.GetArrayAsync($"{organizationPath}/members?max=100", cancellationToken).ConfigureAwait(false);
        foreach (JsonElement member in members)
        {
            string username = RequiredString(member);
            IReadOnlyList<JsonElement> users = await client.GetArrayAsync($"admin/realms/{Escape(options.Realm)}/users?username={Escape(username)}&exact=true", cancellationToken).ConfigureAwait(false);
            JsonElement user = users.Single(candidate => StringEquals(candidate, "username", username));
            string userId = RequiredString(user, "id");
            if (!existingMembers.Any(candidate => StringEquals(candidate, "id", userId)))
            {
                await client.PostAsync($"{organizationPath}/members", JsonSerializer.SerializeToElement(userId), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private ProvisionedOrganization ToProvisionedOrganization(JsonElement organization, Guid organizationId)
    {
        JsonElement tenant = organization.GetProperty("tenant");
        return new ProvisionedOrganization(
            organizationId,
            RequiredString(organization, "alias"),
            RequiredString(tenant, "identifier"),
            RequiredString(tenant, "databaseStrategy"),
            RequiredString(tenant, "databaseProvider"),
            tenant.GetProperty("hasReadReplicas").GetBoolean());
    }

    private Guid ParseOrganizationId(string value, string alias) =>
        Guid.TryParse(value, out Guid result) ? result : throw new InvalidOperationException($"Keycloak organization '{alias}' returned non-GUID identifier '{value}'.");

    private IReadOnlyList<JsonElement> RequiredArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Local organization manifest property '{property}' must be an array.");
        }

        return values.EnumerateArray().Select(value => value.Clone()).ToArray();
    }

    private JsonElement Without(JsonElement element, params string[] excluded)
    {
        var excludedNames = excluded.ToHashSet(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(element.EnumerateObject()
            .Where(property => !excludedNames.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property.Value)));
        return document.RootElement.Clone();
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

        return actual.ValueKind == expected.ValueKind && actual.GetRawText() == expected.GetRawText();
    }

    private string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) ? RequiredString(value) : throw new InvalidOperationException($"Local organization manifest property '{property}' is required.");

    private string RequiredString(JsonElement element) => element.GetString() ?? throw new InvalidOperationException("A required local organization value was empty.");

    private bool StringEquals(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out JsonElement value) && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private string Escape(string value) => Uri.EscapeDataString(value);
}

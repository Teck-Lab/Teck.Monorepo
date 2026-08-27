extern alias OrderHost;
extern alias PricingHost;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SharedKernel.Infrastructure.Endpoints;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Proves the committed local Keycloak realm matches each routed service's permission contract.</summary>
public sealed class RealmPermissionContractTests
{
    private const string GatewayClientId = "public-gateway";
    private const string OrganizationMapperId = "oidc-organization-membership-mapper";
    private const string PlatformManagerRole = "platform-manager";
    private const string PlatformReaderRole = "platform-reader";

    /// <summary>Ensures every endpoint permission has a matching imported Keycloak resource, scope policy, and role policy.</summary>
    [Fact]
    public void CommittedRealm_WhenRoutedEndpointPermissionsAreReflected_DeclaresMatchingResourcesAndScopes()
    {
        using JsonDocument realm = ReadRealm();

        Assert.True(realm.RootElement.GetProperty("organizationsEnabled").GetBoolean());
        Assert.Equal(
            [PlatformManagerRole, PlatformReaderRole],
            realm.RootElement.GetProperty("roles").GetProperty("realm").EnumerateArray()
                .Select(role => role.GetProperty("name").GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());

        AssertPermissionsMatchClient(realm.RootElement, "order-api", typeof(OrderHost::Program).Assembly);
        AssertPermissionsMatchClient(realm.RootElement, "pricing-api", typeof(PricingHost::Program).Assembly);
    }

    /// <summary>Ensures routed clients emit organization membership with organization identifiers.</summary>
    [Fact]
    public void CommittedRealm_WhenRoutedClientsAreImported_MapsOrganizationMembershipAndUsesConfidentialAudiences()
    {
        using JsonDocument realm = ReadRealm();

        foreach (string clientId in new[] { GatewayClientId, "order-api", "pricing-api", "teck-dashboard" })
        {
            AssertOrganizationMapper(FindClient(realm.RootElement, clientId));
        }

        foreach (string clientId in new[] { "order-api", "pricing-api" })
        {
            JsonElement client = FindClient(realm.RootElement, clientId);
            Assert.False(client.GetProperty("publicClient").GetBoolean());
            Assert.True(client.GetProperty("authorizationServicesEnabled").GetBoolean());
        }

        JsonElement gateway = FindClient(realm.RootElement, GatewayClientId);
        Assert.Contains(
            gateway.GetProperty("protocolMappers").EnumerateArray(),
            mapper => mapper.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper" &&
                mapper.GetProperty("config").GetProperty("included.client.audience").GetString() == "pricing-api");

        JsonElement dashboard = FindClient(realm.RootElement, "teck-dashboard");
        Assert.False(dashboard.GetProperty("publicClient").GetBoolean());
        Assert.True(dashboard.GetProperty("standardFlowEnabled").GetBoolean());
        Assert.False(dashboard.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.Equal("S256", dashboard.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());
        Assert.Equal(
            ["http://localhost:3001/*", "http://localhost:3001/api/auth/callback/keycloak"],
            dashboard.GetProperty("redirectUris").EnumerateArray()
                .Select(uri => uri.GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            ["http://localhost:3001"],
            dashboard.GetProperty("webOrigins").EnumerateArray()
                .Select(origin => origin.GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>Ensures every committed realm credential is visibly local-only, except the allow-listed legacy gateway secret.</summary>
    [Fact]
    public void CommittedRealm_WhenCredentialsAreEnumerated_UsesOnlyLocalOnlyValues()
    {
        using JsonDocument realm = ReadRealm();
        var credentials = new List<(string Kind, string Owner, string Value, bool? Temporary)>();

        foreach (JsonElement client in realm.RootElement.GetProperty("clients").EnumerateArray())
        {
            if (client.TryGetProperty("secret", out JsonElement secret))
            {
                credentials.Add(("client secret", client.GetProperty("clientId").GetString()!, secret.GetString()!, null));
            }
        }

        foreach (JsonElement user in realm.RootElement.GetProperty("users").EnumerateArray())
        {
            foreach (JsonElement credential in user.GetProperty("credentials").EnumerateArray())
            {
                credentials.Add((
                    credential.GetProperty("type").GetString()!,
                    user.GetProperty("username").GetString()!,
                    credential.GetProperty("value").GetString()!,
                    credential.GetProperty("temporary").GetBoolean()));
            }
        }

        Assert.Contains(credentials, credential => credential is ("client secret", "teck-dashboard", "local-only-dashboard-secret-not-for-production", null));
        Assert.Contains(credentials, credential => credential is ("client secret", "order-api", "local-only-order-api-secret-not-for-production", null));
        Assert.Contains(credentials, credential => credential is ("client secret", "pricing-api", "local-only-pricing-api-secret-not-for-production", null));
        Assert.Contains(credentials, credential => credential is ("password", "dev@teck.local", "local-only-dev-password-not-for-production", false));
        Assert.Contains(credentials, credential => credential is ("password", "dev-reader@teck.local", "local-only-dev-reader-password-not-for-production", false));

        AssertUserRoles(realm.RootElement, "dev@teck.local", [PlatformManagerRole, PlatformReaderRole]);
        AssertUserRoles(realm.RootElement, "dev-reader@teck.local", [PlatformReaderRole]);

        Assert.All(credentials, credential =>
        {
            if (credential is ("client secret", GatewayClientId, "dev-secret-change-me", null))
            {
                return;
            }

            Assert.StartsWith("local-only-", credential.Value, StringComparison.Ordinal);
            Assert.EndsWith("-not-for-production", credential.Value, StringComparison.Ordinal);
            Assert.Contains(credential.Kind == "client secret" ? "-secret-" : "-password-", credential.Value, StringComparison.Ordinal);
            Assert.False(credential.Temporary ?? false);
        });
    }

    private static void AssertPermissionsMatchClient(JsonElement realm, string clientId, Assembly endpointAssembly)
    {
        JsonElement authorizationSettings = FindClient(realm, clientId).GetProperty("authorizationSettings");
        var scopesByResource = authorizationSettings.GetProperty("resources").EnumerateArray()
            .ToDictionary(
                resource => resource.GetProperty("name").GetString()!,
                resource => resource.GetProperty("scopes").EnumerateArray()
                    .Select(scope => scope.GetProperty("name").GetString()!)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        foreach (EndpointPermission permission in ReadDeclaredPermissions(endpointAssembly))
        {
            Assert.True(scopesByResource.TryGetValue(permission.Resource, out HashSet<string>? scopes));
            Assert.Contains(permission.Scope, scopes!);

            JsonElement scopePolicy = authorizationSettings.GetProperty("policies").EnumerateArray().Single(policy =>
                policy.GetProperty("type").GetString() == "scope" &&
                JsonSerializer.Deserialize<string[]>(policy.GetProperty("config").GetProperty("scopes").GetString()!)!
                    .Contains(permission.Scope, StringComparer.Ordinal));
            string appliedRolePolicy = JsonSerializer.Deserialize<string[]>(scopePolicy.GetProperty("config").GetProperty("applyPolicies").GetString()!)!.Single();
            JsonElement rolePolicy = authorizationSettings.GetProperty("policies").EnumerateArray().Single(policy =>
                policy.GetProperty("name").GetString() == appliedRolePolicy);
            Assert.Equal("role", rolePolicy.GetProperty("type").GetString());
            Assert.Equal("true", rolePolicy.GetProperty("config").GetProperty("fetchRoles").GetString());
            using JsonDocument roles = JsonDocument.Parse(rolePolicy.GetProperty("config").GetProperty("roles").GetString()!);
            string[] roleIds = roles.RootElement.EnumerateArray()
                .Select(role => role.GetProperty("id").GetString()!)
                .ToArray();

            string[] expectedRoleIds = permission.Scope == "read"
                ? [PlatformManagerRole, PlatformReaderRole]
                : [PlatformManagerRole];
            Assert.Equal(
                expectedRoleIds.Order(StringComparer.Ordinal).ToArray(),
                roleIds.Order(StringComparer.Ordinal).ToArray());
        }
    }

    private static void AssertOrganizationMapper(JsonElement client)
    {
        JsonElement mapper = client.GetProperty("protocolMappers").EnumerateArray()
            .Single(item => item.GetProperty("protocolMapper").GetString() == OrganizationMapperId);
        JsonElement config = mapper.GetProperty("config");
        Assert.Equal("organization", config.GetProperty("claim.name").GetString());
        Assert.Equal("true", config.GetProperty("addOrganizationId").GetString());
        Assert.Equal("true", config.GetProperty("access.token.claim").GetString());
    }

    private static void AssertUserRoles(JsonElement realm, string username, string[] expectedRoles)
    {
        JsonElement user = realm.GetProperty("users").EnumerateArray()
            .Single(item => item.GetProperty("username").GetString() == username);
        Assert.Equal(
            expectedRoles.Order(StringComparer.Ordinal).ToArray(),
            user.GetProperty("realmRoles").EnumerateArray()
                .Select(role => role.GetString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static JsonElement FindClient(JsonElement realm, string clientId) => realm.GetProperty("clients").EnumerateArray()
        .Single(client => client.GetProperty("clientId").GetString() == clientId);

    private static EndpointPermission[] ReadDeclaredPermissions(Assembly endpointAssembly) => endpointAssembly
        .GetTypes()
        .Where(type => !type.IsAbstract)
        .Select(type => (Type: type, Permission: type.GetProperty("Permission", BindingFlags.Instance | BindingFlags.NonPublic)))
        .Where(item => item.Permission?.PropertyType == typeof(EndpointPermission))
        .Select(item => (EndpointPermission)item.Permission!.GetValue(RuntimeHelpers.GetUninitializedObject(item.Type))!)
        .Distinct()
        .ToArray();

    private static JsonDocument ReadRealm()
    {
        string root = FindRepositoryRoot();
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "aspire", "Teck.AppHost", "realms", "teck-realm.json")));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "aspire", "Teck.AppHost", "realms", "teck-realm.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the committed Teck realm JSON.");
    }
}

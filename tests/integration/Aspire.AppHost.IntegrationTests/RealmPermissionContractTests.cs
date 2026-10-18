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
    private const string DefaultRealmPath = "src/aspire/Teck.AppHost/realms/teck-realm.json";
    private const string TestRealmPath = "src/aspire/Teck.AppHost/test-realms/teck-realm.json";
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
            ["https://REPLACE_WITH_ENVIRONMENT_DASHBOARD_URL/*", "https://REPLACE_WITH_ENVIRONMENT_DASHBOARD_URL/api/auth/callback/keycloak"],
            dashboard.GetProperty("redirectUris").EnumerateArray()
                .Select(uri => uri.GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            ["https://REPLACE_WITH_ENVIRONMENT_DASHBOARD_URL"],
            dashboard.GetProperty("webOrigins").EnumerateArray()
                .Select(origin => origin.GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>Ensures the production-safe default realm contains no user credentials or enabled password grants.</summary>
    [Fact]
    public void DefaultRealm_WhenCredentialsAreEnumerated_RequiresEnvironmentValuesAndDisablesPasswordGrants()
    {
        using JsonDocument realm = ReadRealm();
        Assert.Empty(realm.RootElement.GetProperty("users").EnumerateArray());

        foreach (JsonElement client in realm.RootElement.GetProperty("clients").EnumerateArray())
        {
            Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean(), client.GetProperty("clientId").GetString());
            if (client.TryGetProperty("secret", out JsonElement secret))
            {
                Assert.StartsWith("REPLACE_WITH_ENVIRONMENT_SECRET_", secret.GetString(), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Ensures test realm preserves authorization contracts and adds only test credentials and grants.</summary>
    [Fact]
    public void TestRealm_WhenComparedToDefault_PreservesResourcesRolesScopesAndMappers()
    {
        using JsonDocument production = ReadRealm();
        using JsonDocument test = ReadTestRealm();
        JsonElement prod = production.RootElement;
        JsonElement testRoot = test.RootElement;

        Assert.Equal(CanonicalJson(prod.GetProperty("roles")), CanonicalJson(testRoot.GetProperty("roles")));
        foreach (JsonElement productionClient in prod.GetProperty("clients").EnumerateArray())
        {
            JsonElement testClient = FindClient(testRoot, productionClient.GetProperty("clientId").GetString()!);
            foreach (string property in new[] { "protocolMappers", "authorizationSettings" })
            {
                bool hasProduction = productionClient.TryGetProperty(property, out JsonElement productionValue);
                bool hasTest = testClient.TryGetProperty(property, out JsonElement testValue);
                Assert.Equal(hasProduction, hasTest);
                if (hasProduction)
                {
                    Assert.Equal(CanonicalJson(productionValue), CanonicalJson(testValue));
                }
            }
            Assert.False(testClient.GetProperty("directAccessGrantsEnabled").GetBoolean());
            Assert.Equal(productionClient.GetProperty("publicClient").GetBoolean(), testClient.GetProperty("publicClient").GetBoolean());
            if (productionClient.TryGetProperty("secret", out JsonElement productionSecret))
            {
                Assert.StartsWith("local-only-test-", testClient.GetProperty("secret").GetString(), StringComparison.Ordinal);
                Assert.StartsWith("REPLACE_WITH_ENVIRONMENT_SECRET_", productionSecret.GetString(), StringComparison.Ordinal);
            }
        }
        JsonElement[] testClients = testRoot.GetProperty("clients").EnumerateArray().ToArray();
        Assert.Equal(prod.GetProperty("clients").GetArrayLength() + 1, testClients.Length);
        JsonElement tokenClient = FindClient(testRoot, "integration-test-client");
        Assert.False(tokenClient.GetProperty("publicClient").GetBoolean());
        Assert.True(tokenClient.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.StartsWith("local-only-test-", tokenClient.GetProperty("secret").GetString(), StringComparison.Ordinal);
        Assert.Contains(tokenClient.GetProperty("protocolMappers").EnumerateArray(), mapper => mapper.GetProperty("protocolMapper").GetString() == OrganizationMapperId);
        foreach (string audience in new[] { "order-api", "pricing-api", GatewayClientId })
        {
            Assert.Contains(tokenClient.GetProperty("protocolMappers").EnumerateArray(), mapper =>
                mapper.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper"
                && mapper.GetProperty("config").GetProperty("included.client.audience").GetString() == audience);
        }
        Assert.Equal(1, testClients.Count(client => client.GetProperty("directAccessGrantsEnabled").GetBoolean()));
        JsonElement testUsers = testRoot.GetProperty("users");
        JsonElement productionUsers = prod.GetProperty("users");
        Assert.Empty(productionUsers.EnumerateArray());
        Assert.Equal(4, testUsers.GetArrayLength());
        Assert.Equal("none", testRoot.GetProperty("sslRequired").GetString());
        Assert.Contains(testUsers.EnumerateArray(), user => user.GetProperty("username").GetString() == "integration-manager");
        Assert.All(testUsers.EnumerateArray(), user => Assert.Contains(user.GetProperty("credentials").EnumerateArray(), credential => credential.GetProperty("type").GetString() == "password"));
    }

    private static string CanonicalJson(JsonElement value) => JsonSerializer.Serialize(value);

    private static JsonDocument ReadTestRealm()
    {
        string root = FindRepositoryRoot();
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root, TestRealmPath)));
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

    /// <summary>Ensures the realm import directory holds only importable realm documents.</summary>
    /// <remarks>
    /// Aspire bind-mounts this directory as Keycloak's data/import folder and Keycloak imports
    /// every JSON file in it as a realm. A non-realm document there aborts the import, so the
    /// container exits 1 and crash-loops, and every resource that waits on Keycloak — the whole
    /// stack — never starts. That is silent: the failure surfaces only as a hang. Keep this
    /// directory free of anything that is not a realm.
    /// </remarks>
    [Fact]
    public void RealmImportDirectory_WhenAspireMountsIt_ContainsOnlyImportableRealmDocuments()
    {
        string importDirectory = Path.Combine(FindRepositoryRoot(), "src", "aspire", "Teck.AppHost", "realms");

        string[] files = Directory.GetFiles(importDirectory);

        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(
                document.RootElement.TryGetProperty("realm", out JsonElement realmName)
                    && !string.IsNullOrWhiteSpace(realmName.GetString()),
                $"'{Path.GetFileName(file)}' sits in Keycloak's import directory but declares no realm. "
                + "Keycloak will fail the whole import and crash-loop. Move it outside this directory.");
        }
    }

    private static JsonDocument ReadRealm()
    {
        string root = FindRepositoryRoot();
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root, DefaultRealmPath)));
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

extern alias OrderHost;
extern alias PricingHost;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SharedKernel.Infrastructure.Endpoints;
using Testcontainers.Keycloak;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Proves the committed local Keycloak realm matches each routed service's permission contract.</summary>
public sealed class RealmPermissionContractTests
{
    private const string CustomerGatewayClientId = "web-customer-gateway";
    private const string AdminGatewayClientId = "web-admin-gateway";
    private const string DefaultRealmPath = "src/aspire/Teck.AppHost/realms/teck-realm.json";
    private const string TestRealmPath = "src/aspire/Teck.AppHost/test-realms/teck-realm.json";
    private const string OrganizationMapperId = "oidc-organization-membership-mapper";
    private const string PlatformManagerRole = "platform-manager";
    private const string PlatformReaderRole = "platform-reader";

    [Fact]
    public async Task TestRealm_WhenFixtureOrganizationsAreProvisioned_IsolatesSignedMembershipAndDeniesNoAccessUser()
    {
        var fixture = new Teck.Platform.IntegrationTests.Shared.KeycloakTestRealmFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(fixture.Container.GetBaseAddress(), UriKind.Absolute) };
            string primaryToken = await fixture.GetUserTokenAsync("integration-manager", "local-only-test-manager-password", "integration-test-client", clientSecret: "local-only-test-integration-token-client-secret-not-for-production").ConfigureAwait(false);
            string foreignToken = await fixture.GetUserTokenAsync("integration-foreign-member", "local-only-test-foreign-password", "integration-test-client", clientSecret: "local-only-test-integration-token-client-secret-not-for-production").ConfigureAwait(false);
            string noAccessToken = await fixture.GetUserTokenAsync("integration-no-access", "local-only-test-no-access-password", "integration-test-client", clientSecret: "local-only-test-integration-token-client-secret-not-for-production").ConfigureAwait(false);

            using JsonDocument primaryPayload = ReadTokenPayload(primaryToken);
            using JsonDocument foreignPayload = ReadTokenPayload(foreignToken);
            AssertSignedAudience(primaryPayload.RootElement);
            AssertSignedAudience(foreignPayload.RootElement);
            Assert.Equal(fixture.OrganizationId, GetSingleOrganizationId(primaryPayload.RootElement));
            Assert.Equal(fixture.ForeignOrganizationId, GetSingleOrganizationId(foreignPayload.RootElement));
            Assert.False(await GetUmaDecisionAsync(http, noAccessToken, "catalog", "supplier-read").ConfigureAwait(false));
        }
        finally
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string GetSingleOrganizationId(JsonElement payload)
    {
        JsonElement memberships = payload.GetProperty("organization");
        return memberships.EnumerateObject()
            .Select(value => value.Value.GetProperty("id").GetString())
            .Single(id => !string.IsNullOrWhiteSpace(id))!;
    }

    private static void AssertSignedAudience(JsonElement payload)
    {
        Assert.True(payload.TryGetProperty("sub", out JsonElement subject));
        Assert.False(string.IsNullOrWhiteSpace(subject.GetString()));
        JsonElement audience = payload.GetProperty("aud");
        Assert.True(audience.ValueKind == JsonValueKind.String
            ? audience.GetString() == "catalog-api"
            : audience.EnumerateArray().Any(value => value.GetString() == "catalog-api"));
    }
    private static JsonDocument ReadTokenPayload(string token)
    {
        string payload = token.Split('.')[1];
        string base64 = payload.Replace('-', '+').Replace('_', '/').PadRight((payload.Length + 3) / 4 * 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(base64));
    }
    /// <summary>Ensures every endpoint permission has a matching imported Keycloak resource, scope policy, and role policy.</summary>
    [Fact]
    public async Task TestRealm_WhenCatalogUmaDecisionIsRequested_AllowsManagerAndDeniesReaderForSupplierRead()
    {
        string root = FindRepositoryRoot();
        string realmPath = Path.Combine(root, TestRealmPath);
        var keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.8.1")
            .WithUsername("admin")
            .WithPassword("local-only-test-keycloak-admin-password")
            .WithRealm(realmPath)
            .Build();
        await keycloak.StartAsync().ConfigureAwait(false);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(keycloak.GetBaseAddress(), UriKind.Absolute) };
            string managerToken = await GetTokenAsync(http, "integration-manager", "local-only-test-manager-password").ConfigureAwait(false);
            string readerToken = await GetTokenAsync(http, "integration-reader", "local-only-test-reader-password").ConfigureAwait(false);

            bool managerSupplierRead = await GetUmaDecisionAsync(http, managerToken, "catalog", "supplier-read").ConfigureAwait(false);
            bool readerSupplierRead = await GetUmaDecisionAsync(http, readerToken, "catalog", "supplier-read").ConfigureAwait(false);
            bool readerCatalogRead = await GetUmaDecisionAsync(http, readerToken, "catalog", "read").ConfigureAwait(false);

            Assert.True(managerSupplierRead);
            Assert.False(readerSupplierRead);
            Assert.True(readerCatalogRead);
        }
        finally
        {
            await keycloak.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task TestRealm_WhenWorkerClientCredentialsAreExchanged_IssuesOnlyTheBillingPurgeFenceIdentity()
    {
        string root = FindRepositoryRoot();
        var keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.8.1")
            .WithUsername("admin")
            .WithPassword("local-only-test-keycloak-admin-password")
            .WithRealm(Path.Combine(root, TestRealmPath))
            .Build();
        await keycloak.StartAsync().ConfigureAwait(false);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(keycloak.GetBaseAddress(), UriKind.Absolute) };
            using JsonDocument workerPayload = await GetClientCredentialsTokenPayloadAsync(http, "tenant-database-worker", "local-only-test-tenant-database-worker-secret-not-for-production", "billing:purge-fence").ConfigureAwait(false);
            Assert.Equal("tenant-database-worker", workerPayload.RootElement.GetProperty("billing_purge_worker_identity").GetString());
            Assert.Contains("billing:purge-fence", workerPayload.RootElement.GetProperty("scope").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Assert.Contains("billing-api", AudienceNamesFromToken(workerPayload.RootElement));

            using JsonDocument billingPayload = await GetClientCredentialsTokenPayloadAsync(http, "billing-api", "local-only-test-billing-api-secret-not-for-production", string.Empty).ConfigureAwait(false);
            Assert.False(billingPayload.RootElement.TryGetProperty("billing_purge_worker_identity", out _));
        }
        finally
        {
            await keycloak.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<JsonDocument> GetClientCredentialsTokenPayloadAsync(HttpClient http, string clientId, string clientSecret, string scope)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };
        if (!string.IsNullOrEmpty(scope))
        {
            form["scope"] = scope;
        }

        using HttpResponseMessage response = await http.PostAsync(
            "/realms/teck/protocol/openid-connect/token",
            new FormUrlEncodedContent(form)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
        return ReadTokenPayload(body.RootElement.GetProperty("access_token").GetString()!);
    }
    private static string[] AudienceNamesFromToken(JsonElement token) => token.GetProperty("aud").ValueKind == JsonValueKind.Array
        ? token.GetProperty("aud").EnumerateArray().Select(value => value.GetString()!).ToArray()
        : [token.GetProperty("aud").GetString()!];

    private static async Task<string> GetTokenAsync(HttpClient http, string username, string password)
    {
        using HttpResponseMessage response = await http.PostAsync(
            "/realms/teck/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "integration-test-client",
                ["client_secret"] = "local-only-test-integration-token-client-secret-not-for-production",
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid organization:* catalog-api-audience",
            })).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<bool> GetUmaDecisionAsync(HttpClient http, string token, string resourceName, string scope)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/realms/teck/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:uma-ticket",
                ["audience"] = "catalog-api",
                ["permission"] = $"{resourceName}#{scope}",
                ["response_mode"] = "decision",
                ["subject_token"] = token,
                ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("catalog-api:local-only-test-catalog-api-secret-not-for-production")));
        using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden) return false;
        response.EnsureSuccessStatusCode();
        using JsonDocument decision = JsonDocument.Parse(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return decision.RootElement.GetProperty("result").GetBoolean();
    }
    [Fact]
    public void CommittedRealm_WhenRoutedEndpointPermissionsAreReflected_DeclaresMatchingResourcesAndScopes()
    {
        using JsonDocument realm = ReadRealm();

        Assert.True(realm.RootElement.GetProperty("organizationsEnabled").GetBoolean());
        Assert.Equal(
            [PlatformManagerRole, PlatformReaderRole, "sales"],
            realm.RootElement.GetProperty("roles").GetProperty("realm").EnumerateArray()
                .Select(role => role.GetProperty("name").GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());

        AssertPermissionsMatchClient(realm.RootElement, "order-api", typeof(OrderHost::Program).Assembly);
        AssertPermissionsMatchClient(realm.RootElement, "pricing-api", typeof(PricingHost::Program).Assembly);
    }

    /// <summary>Each web edge has a distinct confidential exchange client and only its approved downstream audiences.</summary>
    [Theory]
    [InlineData(DefaultRealmPath)]
    [InlineData(TestRealmPath)]
    public void WebGateways_WhenProvisioned_HaveDistinctExchangeAudiences(string realmPath)
    {
        using JsonDocument realm = realmPath == DefaultRealmPath ? ReadRealm() : ReadTestRealm();
        JsonElement customer = FindClient(realm.RootElement, CustomerGatewayClientId);
        JsonElement admin = FindClient(realm.RootElement, AdminGatewayClientId);

        Assert.False(customer.GetProperty("publicClient").GetBoolean());
        Assert.False(admin.GetProperty("publicClient").GetBoolean());
        Assert.Equal("true", customer.GetProperty("attributes").GetProperty("standard.token.exchange.enabled").GetString());
        Assert.Equal("true", admin.GetProperty("attributes").GetProperty("standard.token.exchange.enabled").GetString());
        Assert.Equal(["catalog-api", CustomerGatewayClientId], AudienceNames(customer));
        Assert.Equal(["catalog-api", "pricing-api", AdminGatewayClientId], AudienceNames(admin));
        Assert.Equal(AdminGatewayClientId, AudienceNames(FindClient(realm.RootElement, "teck-dashboard")).Single());
    }

    private static string[] AudienceNames(JsonElement client) =>
        client.GetProperty("protocolMappers").EnumerateArray()
            .Where(mapper => mapper.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper")
            .Select(mapper => mapper.GetProperty("config").GetProperty("included.client.audience").GetString()!)
            .Order(StringComparer.Ordinal).ToArray();

    /// <summary>Ensures routed clients emit organization membership with organization identifiers.</summary>
    [Fact]
    public void CommittedRealm_WhenRoutedClientsAreImported_MapsOrganizationMembershipAndUsesConfidentialAudiences()
    {
        using JsonDocument realm = ReadRealm();

        foreach (string clientId in new[] { CustomerGatewayClientId, AdminGatewayClientId, "order-api", "pricing-api", "teck-dashboard" })
        {
            AssertOrganizationMapper(FindClient(realm.RootElement, clientId));
        }

        foreach (string clientId in new[] { "order-api", "pricing-api" })
        {
            JsonElement client = FindClient(realm.RootElement, clientId);
            Assert.False(client.GetProperty("publicClient").GetBoolean());
            Assert.True(client.GetProperty("authorizationServicesEnabled").GetBoolean());
        }

        Assert.DoesNotContain("pricing-api", AudienceNames(FindClient(realm.RootElement, CustomerGatewayClientId)));
        Assert.Contains("pricing-api", AudienceNames(FindClient(realm.RootElement, AdminGatewayClientId)));

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
        using JsonDocument testRealm = ReadTestRealm();
        JsonElement testDashboard = FindClient(testRealm.RootElement, "teck-dashboard");
        Assert.Equal(
            ["http://localhost:3001/*", "http://localhost:3001/api/auth/callback/keycloak"],
            testDashboard.GetProperty("redirectUris").EnumerateArray()
                .Select(uri => uri.GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>Ensures production has only its credential-free provisioning service account and disables password grants.</summary>
    [Fact]
    public void DefaultRealm_WhenCredentialsAreEnumerated_RequiresEnvironmentValuesAndDisablesPasswordGrants()
    {
        using JsonDocument realm = ReadRealm();
        JsonElement provisioner = Assert.Single(realm.RootElement.GetProperty("users").EnumerateArray());
        Assert.Equal("service-account-customer-org-provisioner", provisioner.GetProperty("username").GetString());
        Assert.Equal("customer-org-provisioner", provisioner.GetProperty("serviceAccountClientId").GetString());
        Assert.False(provisioner.TryGetProperty("credentials", out _));

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
        Assert.Equal(
            new[] {"organization", "basic", "profile", "email", "roles", "web-origins", "tenant-purchase", "order-api-audience", "pricing-api-audience", "web-customer-gateway-audience", "web-admin-gateway-audience", "basket-api-audience", "customer-api-audience", "inventory-api-audience", "billing-api-audience", "catalog-api-audience", "billing:purge-fence"}.Order(StringComparer.Ordinal),
            testRoot.GetProperty("clientScopes").EnumerateArray().Select(scope => scope.GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
        JsonElement organizationScope = testRoot.GetProperty("clientScopes").EnumerateArray().Single(scope => scope.GetProperty("name").GetString() == "organization");
        Assert.Equal("oidc-organization-membership-mapper", organizationScope.GetProperty("protocolMappers")[0].GetProperty("protocolMapper").GetString());
        Assert.Equal("organization", organizationScope.GetProperty("protocolMappers")[0].GetProperty("config").GetProperty("claim.name").GetString());
        Assert.Equal("oidc-hardcoded-claim-mapper", testRoot.GetProperty("clientScopes").EnumerateArray()
            .Single(scope => scope.GetProperty("name").GetString() == "tenant-purchase")
            .GetProperty("protocolMappers")[0].GetProperty("protocolMapper").GetString());
        foreach (JsonElement scope in testRoot.GetProperty("clientScopes").EnumerateArray().Where(scope => scope.GetProperty("name").GetString() is not ("organization" or "basic" or "profile" or "email" or "roles" or "web-origins" or "tenant-purchase" or "billing:purge-fence")))
        {
            string name = scope.GetProperty("name").GetString()!;
            Assert.Equal("oidc-audience-mapper", scope.GetProperty("protocolMappers")[0].GetProperty("protocolMapper").GetString());
            Assert.Equal(name.Replace("-audience", string.Empty, StringComparison.Ordinal),
                scope.GetProperty("protocolMappers")[0].GetProperty("config").GetProperty("included.client.audience").GetString());
        }
        JsonElement billingPurgeScope = testRoot.GetProperty("clientScopes").EnumerateArray().Single(scope => scope.GetProperty("name").GetString() == "billing:purge-fence");
        Assert.Equal("billing_purge_worker_scope", billingPurgeScope.GetProperty("protocolMappers")[0].GetProperty("config").GetProperty("claim.name").GetString());
        string[] expectedOptionalScopes = ["order-api-audience", "pricing-api-audience", "web-customer-gateway-audience", "web-admin-gateway-audience", "basket-api-audience", "customer-api-audience", "inventory-api-audience", "billing-api-audience", "catalog-api-audience"];
        JsonElement integrationTestClient = FindClient(testRoot, "integration-test-client");
        JsonElement optionalScopes = integrationTestClient.GetProperty("optionalClientScopes");
        Assert.Equal(expectedOptionalScopes.Order(StringComparer.Ordinal), optionalScopes.EnumerateArray().Select(scope => scope.GetString()!).Order(StringComparer.Ordinal));
        JsonElement catalog = FindClient(testRoot, "catalog-api").GetProperty("authorizationSettings");
        JsonElement catalogScopes = catalog.GetProperty("resources").EnumerateArray()
            .Single(resource => resource.GetProperty("name").GetString() == "catalog")
            .GetProperty("scopes");
        Assert.Equal(
            new[] {"manage", "read", "supplier-read"},
            catalogScopes.EnumerateArray().Select(scope => scope.GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
        JsonElement supplierReadPolicy = catalog.GetProperty("policies").EnumerateArray()
            .Single(policy => policy.GetProperty("name").GetString() == "supplier-read");
        Assert.Equal("[\"supplier-read\"]", supplierReadPolicy.GetProperty("config").GetProperty("scopes").GetString());
        Assert.Equal("[\"supplier-read-manager\"]", supplierReadPolicy.GetProperty("config").GetProperty("applyPolicies").GetString());
        foreach (JsonElement productionClient in prod.GetProperty("clients").EnumerateArray())
        {
            JsonElement testClient = FindClient(testRoot, productionClient.GetProperty("clientId").GetString()!);
            foreach (string property in new[] { "protocolMappers", "authorizationSettings" })
            {
                bool hasProduction = productionClient.TryGetProperty(property, out JsonElement productionValue);
                bool hasTest = testClient.TryGetProperty(property, out JsonElement testValue);
                Assert.Equal(hasProduction, hasTest);
                if (hasProduction && productionClient.GetProperty("clientId").GetString() != "integration-test-client")
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
        string[] defaultScopes = tokenClient.GetProperty("defaultClientScopes").EnumerateArray().Select(scope => scope.GetString()!).ToArray();
        Assert.Contains("organization", defaultScopes);
        Assert.Contains("basic", defaultScopes);
        Assert.Contains("profile", defaultScopes);
        Assert.DoesNotContain("order-api-audience", defaultScopes);
        Assert.DoesNotContain("pricing-api-audience", defaultScopes);
        string[] expectedTokenOptionalScopes = ["order-api-audience", "pricing-api-audience", "web-customer-gateway-audience", "web-admin-gateway-audience", "basket-api-audience", "customer-api-audience", "inventory-api-audience", "billing-api-audience", "catalog-api-audience"];
        string[] tokenOptionalScopes = tokenClient.GetProperty("optionalClientScopes").EnumerateArray().Select(scope => scope.GetString()!).ToArray();
        Assert.Equal(expectedTokenOptionalScopes.Order(StringComparer.Ordinal), tokenOptionalScopes.Order(StringComparer.Ordinal));
        JsonElement testWorker = FindClient(testRoot, "tenant-database-worker");
        Assert.True(testWorker.GetProperty("serviceAccountsEnabled").GetBoolean());
        Assert.False(testWorker.GetProperty("publicClient").GetBoolean());
        Assert.Equal(["billing:purge-fence"], testWorker.GetProperty("optionalClientScopes").EnumerateArray().Select(scope => scope.GetString()!).ToArray());
        Assert.False(FindClient(testRoot, "billing-api").GetProperty("protocolMappers").EnumerateArray().Any(mapper => mapper.GetProperty("config").TryGetProperty("claim.name", out JsonElement claim) && claim.GetString() == "billing_purge_worker_identity"));
        Assert.Equal(1, testClients.Count(client => client.GetProperty("directAccessGrantsEnabled").GetBoolean()));
        JsonElement testUsers = testRoot.GetProperty("users");
        JsonElement productionUsers = prod.GetProperty("users");
        JsonElement productionProvisioner = Assert.Single(productionUsers.EnumerateArray());
        Assert.Equal("service-account-customer-org-provisioner", productionProvisioner.GetProperty("username").GetString());
        JsonElement testProvisioner = Assert.Single(testUsers.EnumerateArray().Where(user => user.TryGetProperty("serviceAccountClientId", out _)));
        Assert.Equal(CanonicalJson(productionProvisioner), CanonicalJson(testProvisioner));
        Assert.Equal(12, testUsers.EnumerateArray().Count(user => !user.TryGetProperty("serviceAccountClientId", out _)));
        Assert.Equal("none", testRoot.GetProperty("sslRequired").GetString());
        Assert.Contains(testUsers.EnumerateArray(), user => user.GetProperty("username").GetString() == "integration-manager");
        Assert.Contains(testUsers.EnumerateArray(), user => user.GetProperty("username").GetString() == "integration-foreign-manager" && user.GetProperty("realmRoles").EnumerateArray().Any(role => role.GetString() == "platform-manager"));
        Assert.All(testUsers.EnumerateArray().Where(user => !user.TryGetProperty("serviceAccountClientId", out _)), user => Assert.Contains(user.GetProperty("credentials").EnumerateArray(), credential => credential.GetProperty("type").GetString() == "password"));
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

    /// <summary>Starts the real Testcontainers Keycloak realm and verifies issued JWT audiences and organizations.</summary>
    [Fact]
    public async Task TestRealm_WhenRealTokensAreIssued_ContainsExpectedAudienceAndOrganization()
    {
        await using var fixture = new Teck.Platform.IntegrationTests.Shared.KeycloakTestRealmFixture();
        await fixture.InitializeAsync();
        string token = await fixture.GetUserTokenAsync(
            "integration-manager",
            "local-only-test-manager-password",
            "integration-test-client",
            clientSecret: "local-only-test-integration-token-client-secret-not-for-production");

        using JsonDocument payload = DecodeToken(token);
        JsonElement audiences = payload.RootElement.GetProperty("aud");
        string[] values = audiences.ValueKind == JsonValueKind.Array
            ? audiences.EnumerateArray().Select(value => value.GetString()!).ToArray()
            : [audiences.GetString()!];
        Assert.Contains("order-api", values);
        Assert.Contains("pricing-api", values);
        Assert.True(payload.RootElement.TryGetProperty("organization", out JsonElement organizations), "Token must include the Keycloak organization membership claim.");
        Assert.Contains(organizations.EnumerateObject(), organization => organization.Value.GetProperty("id").GetString() == fixture.OrganizationId);

        string narrowToken = await fixture.GetUserTokenAsync(
            "integration-manager",
            "local-only-test-manager-password",
            "integration-test-client",
            clientSecret: "local-only-test-integration-token-client-secret-not-for-production",
            requestedScope: "catalog-api-audience");
        using JsonDocument narrowPayload = DecodeToken(narrowToken);
        JsonElement narrowAudiences = narrowPayload.RootElement.GetProperty("aud");
        string[] narrowValues = narrowAudiences.ValueKind == JsonValueKind.Array
            ? narrowAudiences.EnumerateArray().Select(value => value.GetString()!).ToArray()
            : [narrowAudiences.GetString()!];
        Assert.DoesNotContain("order-api", narrowValues);
        Assert.DoesNotContain("pricing-api", narrowValues);
        Assert.Equal(new[] { "catalog-api" }, narrowValues);
        Assert.Contains(narrowPayload.RootElement.GetProperty("organization").EnumerateObject(), organization => organization.Value.GetProperty("id").GetString() == fixture.OrganizationId);
    }

    private static JsonDocument DecodeToken(string token)
    {
        string payload = token.Split('.')[1];
        string base64 = payload.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(base64));
    }

}

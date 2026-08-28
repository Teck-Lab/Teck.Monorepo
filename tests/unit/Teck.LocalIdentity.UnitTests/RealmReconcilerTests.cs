using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Teck.LocalIdentity;
using Xunit;

namespace Teck.LocalIdentity.UnitTests;

/// <summary>Verifies reconciliation against the Keycloak Admin REST API without requiring Docker.</summary>
public sealed class RealmReconcilerTests
{
    /// <summary>Ensures applying the same committed realm twice converges without creating duplicate Keycloak objects.</summary>
    [Fact]
    public async Task ReconcileAsync_WhenManifestIsAppliedTwice_ConvergesWithoutDuplicateObjects()
    {
        using var handler = new KeycloakAdminApiFake();
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://keycloak.test/") };
        var options = new LocalIdentityOptions
        {
            BaseUrl = client.BaseAddress.ToString(),
            AdminPassword = "local-only-test-password-not-for-production",
        };
        var reconciler = new RealmReconciler(client, options);

        using JsonDocument manifest = ReadCommittedManifest();

        await reconciler.ReconcileAsync(manifest, TestContext.Current.CancellationToken);
        KeycloakState firstState = handler.State;
        handler.ResetMutatingRequestCount();

        await reconciler.ReconcileAsync(manifest, TestContext.Current.CancellationToken);

        Assert.Equal(firstState.OrganizationsEnabled, handler.State.OrganizationsEnabled);
        Assert.Equal(firstState.Roles, handler.State.Roles);
        Assert.Equal(firstState.Clients, handler.State.Clients);
        Assert.Equal(firstState.Users, handler.State.Users);
        Assert.Equal(2, handler.State.Roles.Length);
        Assert.Equal(5, handler.State.Clients.Length);
        Assert.Equal(2, handler.State.Users.Length);
        Assert.True(handler.State.OrganizationsEnabled);
        Assert.True(handler.MutatingRequestCount == 0, $"Unexpected mutations: {string.Join(", ", handler.MutatingRequests.Select(request => $"{request.Method} {request.Path}"))}");
        Assert.Equal(2, handler.CreatedUsersWithCredentials);
    }

    /// <summary>Ensures retained resource-server settings are repaired through the supported base endpoint and then converge.</summary>
    [Fact]
    public async Task ReconcileAsync_WhenAuthorizationSettingsDrift_UpdatesBaseResourceServerAndThenConverges()
    {
        using var handler = new KeycloakAdminApiFake();
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://keycloak.test/") };
        var options = new LocalIdentityOptions
        {
            BaseUrl = client.BaseAddress.ToString(),
            AdminPassword = "local-only-test-password-not-for-production",
        };
        var reconciler = new RealmReconciler(client, options);
        using JsonDocument manifest = ReadCommittedManifest();

        await reconciler.ReconcileAsync(manifest, TestContext.Current.CancellationToken);
        handler.DriftAuthorizationSettings();
        handler.ResetMutatingRequestCount();

        await reconciler.ReconcileAsync(manifest, TestContext.Current.CancellationToken);

        Assert.Contains(handler.MutatingRequests, request => request.Method == HttpMethod.Put && request.Path.EndsWith("/authz/resource-server", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.MutatingRequests, request => request.Path.EndsWith("/settings", StringComparison.Ordinal));
        handler.ResetMutatingRequestCount();

        await reconciler.ReconcileAsync(manifest, TestContext.Current.CancellationToken);

        Assert.True(handler.MutatingRequestCount == 0, $"Unexpected mutations: {string.Join(", ", handler.MutatingRequests.Select(request => $"{request.Method} {request.Path}"))}");
    }

    private static JsonDocument ReadCommittedManifest()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "src", "aspire", "Teck.AppHost", "realms", "teck-realm.json");
            if (File.Exists(path))
            {
                return JsonDocument.Parse(File.ReadAllText(path));
            }
        }

        throw new FileNotFoundException("The committed Keycloak realm manifest was not found.");
    }

    private sealed class KeycloakAdminApiFake : HttpMessageHandler
    {
        private readonly Dictionary<string, Dictionary<string, JsonElement>> _collections = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement> _settings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement> _credentials = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _userRoles = new(StringComparer.Ordinal);
        private JsonElement _realm;
        private int _nextId;

        internal KeycloakState State => new(
            _realm.ValueKind != JsonValueKind.Undefined && _realm.GetProperty("organizationsEnabled").GetBoolean(),
            Collection("/admin/realms/teck/roles").Keys.Order(StringComparer.Ordinal).ToArray(),
            Collection("/admin/realms/teck/clients").Keys.Order(StringComparer.Ordinal).ToArray(),
            Collection("/admin/realms/teck/users").Keys.Order(StringComparer.Ordinal).ToArray());

        internal int MutatingRequestCount { get; private set; }

        internal int CreatedUsersWithCredentials { get; private set; }

        internal List<(HttpMethod Method, string Path)> MutatingRequests { get; } = [];

        internal void ResetMutatingRequestCount()
        {
            MutatingRequestCount = 0;
            MutatingRequests.Clear();
        }

        internal void DriftAuthorizationSettings()
        {
            foreach (string path in _settings.Keys.ToArray())
            {
                using JsonDocument document = JsonDocument.Parse("""{"policyEnforcementMode":"DISABLED"}""");
                _settings[path] = document.RootElement.Clone();
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath.TrimEnd('/');
            if (path == "/realms/master/protocol/openid-connect/token")
            {
                return Json(HttpStatusCode.OK, """{"access_token":"test-token"}""");
            }

            if (path == "/admin/realms/teck")
            {
                if (request.Method == HttpMethod.Get)
                {
                    return _realm.ValueKind == JsonValueKind.Undefined ? NotFound() : Json(HttpStatusCode.OK, _realm);
                }

                _realm = await ReadJsonAsync(request, cancellationToken).ConfigureAwait(false);
                return Empty(HttpStatusCode.NoContent);
            }

            if (path == "/admin/realms" && request.Method == HttpMethod.Post)
            {
                _realm = await ReadJsonAsync(request, cancellationToken).ConfigureAwait(false);
                return Empty(HttpStatusCode.Created);
            }

            if (path.EndsWith("/role-mappings/realm", StringComparison.Ordinal))
            {
                string userId = path.Split('/')[^3];
                if (request.Method == HttpMethod.Get)
                {
                    JsonElement[] roles = _userRoles.GetValueOrDefault(userId, []).Select(name => Collection("/admin/realms/teck/roles")[name]).ToArray();
                    return Json(HttpStatusCode.OK, ToJson(roles));
                }

                JsonElement input = await ReadJsonAsync(request, cancellationToken).ConfigureAwait(false);
                var assigned = _userRoles.GetValueOrDefault(userId, []);
                foreach (JsonElement role in input.EnumerateArray())
                {
                    assigned.Add(role.GetProperty("name").GetString()!);
                }

                _userRoles[userId] = assigned;
                return Empty(HttpStatusCode.NoContent);
            }

            if (path.EndsWith("/reset-password", StringComparison.Ordinal))
            {
                RecordMutation(request.Method, path);
                string userId = path.Split('/')[^2];
                JsonElement credential = await ReadJsonAsync(request, cancellationToken).ConfigureAwait(false);
                if (!_credentials.TryGetValue(userId, out JsonElement current) || current.GetRawText() != credential.GetRawText())
                {
                    _credentials[userId] = credential;
                }

                return Empty(HttpStatusCode.NoContent);
            }

            if (path.EndsWith("/settings", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get)
                {
                    return _settings.TryGetValue(path, out JsonElement existingSettings) ? Json(HttpStatusCode.OK, existingSettings) : NotFound();
                }

                return Empty(HttpStatusCode.MethodNotAllowed);
            }

            if (path.EndsWith("/authz/resource-server", StringComparison.Ordinal))
            {
                if (request.Method != HttpMethod.Put)
                {
                    return Empty(HttpStatusCode.MethodNotAllowed);
                }

                RecordMutation(request.Method, path);
                _settings[$"{path}/settings"] = await ReadJsonAsync(request, cancellationToken).ConfigureAwait(false);
                return Empty(HttpStatusCode.NoContent);
            }

            string collectionPath = CollectionPath(path);
            string? key = ItemKey(path, collectionPath, request.RequestUri.Query);
            var collection = Collection(collectionPath);
            if (request.Method == HttpMethod.Get)
            {
                if (!string.IsNullOrEmpty(request.RequestUri.Query))
                {
                    return Json(HttpStatusCode.OK, ToJson(collection.Values.Where(value => Identity(collectionPath, value) == key)));
                }

                if (key is null)
                {
                    return Json(HttpStatusCode.OK, ToJson(collection.Values));
                }

                return collection.TryGetValue(key, out JsonElement value) ? Json(HttpStatusCode.OK, value) : NotFound();
            }

            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                RecordMutation(request.Method, path);
                JsonElement input = await ReadJsonAsync(request, cancellationToken).ConfigureAwait(false);
                key ??= Identity(collectionPath, input);
                if (request.Method == HttpMethod.Post && collectionPath.EndsWith("/users", StringComparison.Ordinal) && input.TryGetProperty("credentials", out JsonElement credentials) && credentials.GetArrayLength() > 0)
                {
                    CreatedUsersWithCredentials++;
                }
                JsonElement stored = WithId(input, collection.TryGetValue(key, out JsonElement existing) ? existing.GetProperty("id").GetString()! : $"id-{++_nextId}");
                if (!collection.TryGetValue(key, out existing) || existing.GetRawText() != stored.GetRawText())
                {
                    collection[key] = stored;
                }

                return Empty(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
            }

            return NotFound();
        }

        private void RecordMutation(HttpMethod method, string path)
        {
            if (method == HttpMethod.Post || method == HttpMethod.Put)
            {
                MutatingRequestCount++;
                MutatingRequests.Add((method, path));
            }
        }

        private Dictionary<string, JsonElement> Collection(string path)
        {
            if (!_collections.TryGetValue(path, out Dictionary<string, JsonElement>? collection))
            {
                collection = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                _collections[path] = collection;
            }

            return collection;
        }

        private static string CollectionPath(string path)
        {
            foreach (string suffix in new[] { "/protocol-mappers/models", "/authz/resource-server/resource", "/authz/resource-server/policy" })
            {
                if (path.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return path;
                }
            }

            int lastSlash = path.LastIndexOf('/');
            string finalSegment = path[(lastSlash + 1)..];
            return finalSegment is "roles" or "clients" or "users" ? path : path[..lastSlash];
        }

        private static string? ItemKey(string path, string collectionPath, string query)
        {
            if (!string.IsNullOrEmpty(query))
            {
                string value = query.Split('&').First(part => part.Contains('=', StringComparison.Ordinal)).Split('=')[1];
                return Uri.UnescapeDataString(value);
            }

            return path == collectionPath ? null : path[(collectionPath.Length + 1)..];
        }

        private static string Identity(string collectionPath, JsonElement input)
        {
            string property = collectionPath.EndsWith("/clients", StringComparison.Ordinal) ? "clientId" :
                collectionPath.EndsWith("/users", StringComparison.Ordinal) ? "username" : "name";
            return input.GetProperty(property).GetString() ?? throw new InvalidOperationException("Fake input has no identity.");
        }

        private static JsonElement WithId(JsonElement input, string id)
        {
            JsonObject node = JsonNode.Parse(input.GetRawText())!.AsObject();
            node["id"] = id;
            using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
            return document.RootElement.Clone();
        }

        private static JsonElement ToJson(IEnumerable<JsonElement> values)
        {
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(values));
            return document.RootElement.Clone();
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static HttpResponseMessage Empty(HttpStatusCode statusCode) => new(statusCode);

        private static HttpResponseMessage NotFound() => Empty(HttpStatusCode.NotFound);

        private static HttpResponseMessage Json(HttpStatusCode statusCode, JsonElement element) =>
            new(statusCode) { Content = new StringContent(element.GetRawText(), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) =>
            new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }

    private sealed record KeycloakState(bool OrganizationsEnabled, string[] Roles, string[] Clients, string[] Users);
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Teck.LocalIdentity;
using Xunit;

namespace Teck.LocalIdentity.UnitTests;

/// <summary>Verifies local Keycloak organization reconciliation against an in-process Admin API fake.</summary>
public sealed class OrganizationReconcilerTests
{
    /// <summary>Ensures the committed organizations create memberships, read generated identifiers, and converge on a second run.</summary>
    [Fact]
    public async Task ReconcileAsync_WhenCommittedOrganizationsAreAppliedTwice_CreatesMembershipsAndReadsGeneratedIdentifiers()
    {
        using var handler = new OrganizationAdminApiFake();
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://keycloak.test/") };
        var options = new LocalIdentityOptions
        {
            BaseUrl = client.BaseAddress.ToString(),
            AdminPassword = "local-only-test-password-not-for-production",
        };
        using JsonDocument manifest = ReadCommittedOrganizationManifest();
        var reconciler = new OrganizationReconciler(client, options);

        IReadOnlyList<ProvisionedOrganization> firstRun = await reconciler.ReconcileAsync(manifest, TestContext.Current.CancellationToken);
        IReadOnlyList<ProvisionedOrganization> secondRun = await reconciler.ReconcileAsync(manifest, TestContext.Current.CancellationToken);

        Assert.Equal(firstRun, secondRun);
        Assert.Equal(2, firstRun.Count);
        Assert.Equal(["teck-local-alpha", "teck-local-beta"], firstRun.Select(organization => organization.Alias).Order().ToArray());
        Assert.All(firstRun, organization => Assert.NotEqual(Guid.Empty, organization.Id));
        Assert.Equal(2, handler.Organizations.Count);
        Assert.Equal(2, handler.Memberships["teck-local-alpha"].Count);
        Assert.Single(handler.Memberships["teck-local-beta"]);
    }

    private static JsonDocument ReadCommittedOrganizationManifest()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "src", "aspire", "Teck.AppHost", "realms", "local-organizations.json");
            if (File.Exists(path))
            {
                return JsonDocument.Parse(File.ReadAllText(path));
            }
        }

        throw new FileNotFoundException("The committed local organization manifest was not found.");
    }

    private sealed class OrganizationAdminApiFake : HttpMessageHandler
    {
        private readonly Dictionary<string, JsonElement> organizations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> users = new(StringComparer.Ordinal)
        {
            ["dev@teck.local"] = Guid.NewGuid(),
            ["dev-reader@teck.local"] = Guid.NewGuid(),
        };

        internal Dictionary<string, JsonElement> Organizations => organizations;

        internal Dictionary<string, HashSet<Guid>> Memberships { get; } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath.TrimEnd('/');
            if (path == "/realms/master/protocol/openid-connect/token")
            {
                return Json(HttpStatusCode.OK, """{"access_token":"test-token"}""");
            }

            if (path == "/admin/realms/teck/organizations")
            {
                if (request.Method == HttpMethod.Get)
                {
                    string search = QueryValue(request.RequestUri, "search");
                    return Json(HttpStatusCode.OK, ToJson(organizations.Values.Where(organization =>
                        string.Equals(organization.GetProperty("name").GetString(), search, StringComparison.Ordinal) ||
                        organization.GetProperty("domains").EnumerateArray().Any(domain => string.Equals(domain.GetProperty("name").GetString(), search, StringComparison.Ordinal)))));
                }

                JsonElement organization = await ReadJsonAsync(request, cancellationToken).ConfigureAwait(false);
                string alias = organization.GetProperty("alias").GetString()!;
                organizations[alias] = WithId(organization, Guid.NewGuid());
                Memberships.TryAdd(alias, []);
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (path == "/admin/realms/teck/users")
            {
                string username = QueryValue(request.RequestUri, "username");
                return users.TryGetValue(username, out Guid userId)
                    ? Json(HttpStatusCode.OK, ToJson([JsonSerializer.SerializeToElement(new { id = userId, username })]))
                    : Json(HttpStatusCode.OK, ToJson([]));
            }

            if (path.StartsWith("/admin/realms/teck/organizations/", StringComparison.Ordinal) && path.EndsWith("/members", StringComparison.Ordinal))
            {
                string id = path.Split('/')[^2];
                KeyValuePair<string, JsonElement> organization = organizations.Single(pair => pair.Value.GetProperty("id").GetString() == id);
                if (request.Method == HttpMethod.Get)
                {
                    return Json(HttpStatusCode.OK, ToJson(Memberships[organization.Key].Select(memberId => JsonSerializer.SerializeToElement(new { id = memberId }))));
                }

                JsonElement userId = await ReadJsonAsync(request, cancellationToken).ConfigureAwait(false);
                Memberships[organization.Key].Add(Guid.Parse(userId.GetString()!));
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string QueryValue(Uri uri, string name) =>
            Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&').Single(part => part.StartsWith($"{name}=", StringComparison.Ordinal))[(name.Length + 1)..]);

        private static JsonElement WithId(JsonElement value, Guid id)
        {
            JsonObject node = JsonNode.Parse(value.GetRawText())!.AsObject();
            node["id"] = id.ToString();
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

        private static HttpResponseMessage Json(HttpStatusCode statusCode, JsonElement element) =>
            new(statusCode) { Content = new StringContent(element.GetRawText(), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) =>
            new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
}

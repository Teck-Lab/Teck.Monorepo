using System.Text.Json;
using Xunit;

namespace Gateway.Public.UnitTests.Edge;

/// <summary>Verifies gateway token-exchange audiences agree with committed realm and downstream service configuration.</summary>
public sealed class RoutedAudienceContractTests
{
    /// <summary>Ensures every routed audience exists in the realm and matches the target service's validated audience.</summary>
    [Fact]
    public void CommittedConfiguration_WhenGatewayRoutesAreEnumerated_MatchesRealmAndDownstreamAudiences()
    {
        using JsonDocument realm = ReadJson("src", "aspire", "Teck.AppHost", "realms", "teck-realm.json");
        using JsonDocument gateway = ReadJson("src", "services", "gateway", "public", "appsettings.json");
        using JsonDocument order = ReadJson("src", "services", "commerce", "order", "Order.Host", "appsettings.Development.json");
        using JsonDocument pricing = ReadJson("src", "services", "commerce", "pricing", "Pricing.Host", "appsettings.Development.json");
        HashSet<string> realmClientIds = realm.RootElement.GetProperty("clients").EnumerateArray()
            .Select(client => client.GetProperty("clientId").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var resources = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["order"] = order.RootElement,
            ["pricing"] = pricing.RootElement,
        };

        foreach (JsonProperty cluster in gateway.RootElement.GetProperty("ReverseProxy").GetProperty("Clusters").EnumerateObject())
        {
            string audience = cluster.Value.GetProperty("Destinations").GetProperty("primary")
                .GetProperty("AccessTokenClientName").GetString()!;
            JsonElement downstream = resources[cluster.Name];

            Assert.Contains(audience, realmClientIds);
            Assert.Equal(downstream.GetProperty("Keycloak").GetProperty("resource").GetString(), audience);
        }
    }

    /// <summary>Ensures each routed resource server uses its matching committed local-only Keycloak secret.</summary>
    [Fact]
    public void CommittedConfiguration_WhenResourceServerSecretsAreEnumerated_MatchesTheRealmAndLocalOnlyConvention()
    {
        using JsonDocument realm = ReadJson("src", "aspire", "Teck.AppHost", "realms", "teck-realm.json");
        using JsonDocument order = ReadJson("src", "services", "commerce", "order", "Order.Host", "appsettings.Development.json");
        using JsonDocument pricing = ReadJson("src", "services", "commerce", "pricing", "Pricing.Host", "appsettings.Development.json");

        foreach (JsonElement service in new[] { order.RootElement, pricing.RootElement })
        {
            JsonElement keycloak = service.GetProperty("Keycloak");
            string clientId = keycloak.GetProperty("resource").GetString()!;
            string secret = keycloak.GetProperty("credentials").GetProperty("secret").GetString()!;
            JsonElement realmClient = realm.RootElement.GetProperty("clients").EnumerateArray()
                .Single(client => client.GetProperty("clientId").GetString() == clientId);

            Assert.Equal(realmClient.GetProperty("secret").GetString(), secret);
            Assert.StartsWith("local-only-", secret, StringComparison.Ordinal);
            Assert.EndsWith("-secret-not-for-production", secret, StringComparison.Ordinal);
        }
    }

    private static JsonDocument ReadJson(params string[] relativePath) => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), Path.Combine(relativePath))));

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

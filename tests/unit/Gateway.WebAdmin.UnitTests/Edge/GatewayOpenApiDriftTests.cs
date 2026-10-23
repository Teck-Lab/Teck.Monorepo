using System.Text.Json;
using Xunit;

namespace Gateway.WebAdmin.UnitTests.Edge;

/// <summary>Checks the v1 gateway contracts against their actual YARP path and method allowlists.</summary>
public sealed class GatewayOpenApiDriftTests
{
    /// <summary>Every documented operation must reach the intended cluster and every configured route must have a documented operation.</summary>
    [Theory]
    [InlineData("web-customer", "gateway-customer-v1.openapi.json")]
    [InlineData("web-admin", "gateway-admin-v1.openapi.json")]
    public void GatewayContract_ExactlyMatchesReachableYarpOperations(string host, string contract)
    {
        string root = FindRoot();
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "services", "gateway", host, "appsettings.json")));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "services", "gateway", "shared", "Contracts", contract)));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "services", "gateway", "shared", "Contracts", "gateway-v1.operations.json")));
        JsonElement routes = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        JsonElement paths = document.RootElement.GetProperty("paths");
        string audience = host == "web-customer" ? "customer" : "admin";
        var operations = manifest.RootElement.GetProperty(audience).EnumerateArray()
            .Select(operation => (
                Path: operation.GetProperty("path").GetString()!,
                Method: operation.GetProperty("method").GetString()!,
                Service: operation.GetProperty("service").GetString()!)).ToArray();
        int documentedCount = paths.EnumerateObject().Sum(path => path.Value.EnumerateObject().Count());
        Assert.Equal(operations.Length, documentedCount);
        Assert.Equal(operations.Length, operations.Distinct().Count());

        foreach (var (path, method, service) in operations)
        {
            Assert.True(paths.TryGetProperty(path, out JsonElement item) && item.TryGetProperty(method, out _),
                $"Missing documented operation {method.ToUpperInvariant()} {path}");
            Assert.Contains(routes.EnumerateObject(), route =>
                route.Value.GetProperty("ClusterId").GetString() == service &&
                Covers(route.Value.GetProperty("Match"), path, method));
        }

        foreach (JsonProperty route in routes.EnumerateObject())
        {
            JsonElement match = route.Value.GetProperty("Match");
            Assert.Contains(operations, operation =>
                operation.Service == route.Value.GetProperty("ClusterId").GetString() &&
                Covers(match, operation.Path, operation.Method));
        }
    }

    private static bool Covers(JsonElement match, string path, string method)
    {
        string pattern = match.GetProperty("Path").GetString()!;
        string prefix = pattern.Replace("{**catch-all}", string.Empty, StringComparison.Ordinal);
        bool pathMatches = pattern == path || pattern.EndsWith("{**catch-all}", StringComparison.Ordinal) &&
            path.StartsWith(prefix, StringComparison.Ordinal);
        return pathMatches && (!match.TryGetProperty("Methods", out JsonElement methods) ||
            methods.EnumerateArray().Any(value => value.GetString()?.Equals(method, StringComparison.OrdinalIgnoreCase) == true));
    }

    private static string FindRoot()
    {
        string? root = Directory.GetCurrentDirectory();
        while (root is not null && !File.Exists(Path.Combine(root, "Teck.Platform.slnx")))
        {
            root = Directory.GetParent(root)?.FullName;
        }

        return root ?? throw new InvalidOperationException("Solution root not found.");
    }
}

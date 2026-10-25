using System.Text.Json;
using Xunit;

namespace Gateway.WebAdmin.UnitTests.Edge;

/// <summary>Protects the customer route allowlist and admin-only namespaces.</summary>
public sealed class GatewayRouteSafetyTests
{
    /// <summary>Customer host exposes only the specifically approved catalog reads.</summary>
    [Fact]
    public void CustomerRoutes_AllowOnlyProductListAndDetailReads()
    {
        using JsonDocument config = ReadGateway("web-customer");
        JsonElement routes = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        var actual = routes.EnumerateObject().Select(route => (
            Path: route.Value.GetProperty("Match").GetProperty("Path").GetString(),
            Methods: string.Join(',', route.Value.GetProperty("Match").GetProperty("Methods")
                .EnumerateArray().Select(method => method.GetString())))).ToArray();

        Assert.Equal(2, actual.Length);
        Assert.Contains(("/api/v1/catalog/products", "GET"), actual);
        Assert.Contains(("/api/v1/catalog/products/{productId}", "GET"), actual);
        Assert.All(routes.EnumerateObject(), route =>
        {
            Assert.Equal("catalog", route.Value.GetProperty("ClusterId").GetString());
            Assert.Equal("authenticated", route.Value.GetProperty("AuthorizationPolicy").GetString());
            Assert.Equal("Authenticated", route.Value.GetProperty("Metadata")
                .GetProperty("EdgeAccess").GetString());
        });
    }

    /// <summary>Staff price editing and shared catalog reads are explicitly reachable on admin, not customer.</summary>
    [Fact]
    public void AdminRoutes_IncludeSharedProductsAndStaffPriceEditing()
    {
        using JsonDocument config = ReadGateway("web-admin");
        JsonElement routes = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        string[] paths = routes.EnumerateObject().Select(route =>
            route.Value.GetProperty("Match").GetProperty("Path").GetString()!).ToArray();

        Assert.Contains("/api/v1/catalog/products", paths);
        Assert.Contains("/api/v1/admin/catalog/{**catch-all}", paths);
        Assert.Contains("/api/v1/admin/pricing/{**catch-all}", paths);
        Assert.All(routes.EnumerateObject().Where(route =>
            route.Value.GetProperty("Match").GetProperty("Path").GetString()!
                .StartsWith("/api/v1/admin/", StringComparison.Ordinal)),
            route => Assert.Equal("operator", route.Value.GetProperty("AuthorizationPolicy").GetString()));
        Assert.DoesNotContain(paths, path => path.StartsWith("/products", StringComparison.Ordinal) ||
            path.StartsWith("/price-lists", StringComparison.Ordinal));
    }

    private static JsonDocument ReadGateway(string host)
    {
        string? root = Directory.GetCurrentDirectory();
        while (root is not null && !File.Exists(Path.Combine(root, "Teck.Platform.slnx")))
        {
            root = Directory.GetParent(root)?.FullName;
        }

        Assert.NotNull(root);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, "src", "services", "gateway", host, "appsettings.json")));
    }
}

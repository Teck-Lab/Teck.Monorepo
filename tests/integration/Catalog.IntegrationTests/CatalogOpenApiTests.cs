using System.Net;
using System.Text.Json;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Catalog.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class CatalogOpenApiTests : CatalogIntegrationTestBase
{
    public CatalogOpenApiTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task HostOpenApi_SeparatesCustomerProductsFromOperatorRoutes()
    {
        using HttpResponseMessage customerResponse = await Client.GetAsync(new Uri("/api/v1/catalog/products", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, customerResponse.StatusCode);
        using HttpResponseMessage legacyResponse = await Client.GetAsync(new Uri("/products", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, legacyResponse.StatusCode);

        using HttpResponseMessage response = await Client.GetAsync(new Uri("/openapi/v1/openapi.json", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");
        JsonElement operation = paths.GetProperty("/api/v1/catalog/products").GetProperty("get");
        Assert.Equal("CatalogHostEndpointsProductsListProductsEndpoint", operation.GetProperty("operationId").GetString());
        Assert.Equal("#/components/schemas/CatalogApplicationProductsResponsesProductSummaryDto",
            operation.GetProperty("responses").GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema").GetProperty("items")
                .GetProperty("$ref").GetString());
        Assert.True(paths.GetProperty("/api/v1/catalog/products/{productId}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/admin/catalog/products").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/v1/admin/catalog/suppliers/{supplierId}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/admin/catalog/variants/{variantId}/suppliers/{supplierId}/cost").TryGetProperty("put", out _));
        Assert.False(paths.TryGetProperty("/products", out _));
    }

    [Fact]
    public async Task CustomerOpenApi_ExcludesOperatorManagementAndSupplierCosts()
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri("/openapi/v1-public/openapi.json", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");
        Assert.Equal(2, paths.EnumerateObject().Count());
        Assert.True(paths.GetProperty("/api/v1/catalog/products").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/catalog/products/{productId}").TryGetProperty("get", out _));
        Assert.False(paths.TryGetProperty("/api/v1/admin/catalog/products", out _));
        Assert.False(paths.TryGetProperty("/api/v1/admin/catalog/suppliers/{supplierId}", out _));
        Assert.False(paths.TryGetProperty("/api/v1/admin/catalog/variants/{variantId}/suppliers/{supplierId}/cost", out _));
    }
}

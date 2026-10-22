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
    public async Task HostOpenApi_DescribesActualProductResponse()
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri("/openapi/v1/openapi.json", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/products").GetProperty("get");
        Assert.Equal("CatalogHostEndpointsProductsListProductsEndpoint", operation.GetProperty("operationId").GetString());
        Assert.Equal("#/components/schemas/CatalogApplicationProductsResponsesProductSummaryDto",
            operation.GetProperty("responses").GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema").GetProperty("items")
                .GetProperty("$ref").GetString());
    }
}

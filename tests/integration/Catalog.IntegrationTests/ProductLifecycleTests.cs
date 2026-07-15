// <copyright file="ProductLifecycleTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Catalog.Application.Products.Responses;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Catalog.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class ProductLifecycleTests : CatalogIntegrationTestBase
{
    public ProductLifecycleTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CreateProduct_ReturnsCreated_WithDefaultVariant()
    {
        var response = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Widget",
            Description = "A widget",
            CategoryId = (Guid?)null,
            Sku = "WIDGET-1",
            SellPriceAmount = 9.99m,
            SellPriceCurrency = "USD",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);
        Assert.Equal("Widget", product!.Name);
        Assert.NotEqual(Guid.Empty, product.Id);
        var variant = Assert.Single(product.Variants);
        Assert.True(variant.IsDefault);
    }
}

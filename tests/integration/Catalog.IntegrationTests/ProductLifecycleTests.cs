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

    [Fact]
    public async Task GetProduct_AfterCreate_ReturnsProduct()
    {
        var created = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Gadget", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "GADGET-1", SellPriceAmount = 5m, SellPriceCurrency = "USD",
        });
        var product = await created.Content.ReadFromJsonAsync<ProductDto>();

        var fetched = await Client.GetAsync(new Uri($"/products/{product!.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var body = await fetched.Content.ReadFromJsonAsync<ProductDto>();
        Assert.Equal(product.Id, body!.Id);
        Assert.Equal("Gadget", body.Name);
    }
}

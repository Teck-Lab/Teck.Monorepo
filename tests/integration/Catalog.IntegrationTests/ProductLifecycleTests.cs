// <copyright file="ProductLifecycleTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Catalog.Application.Database;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Finbuckle.MultiTenant.Extensions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
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

    [Fact]
    public async Task GetProduct_ForeignTenantProduct_IsExcluded()
    {
        var foreignProduct = Product.Create(
            "tenant-b",
            "Foreign widget",
            "Foreign tenant only",
            null,
            "FOREIGN-WIDGET",
            new Money(9.99m, "USD"));

        await using (var seed = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(DatabaseConnectionString)
                .UseTeckCloudTenant("tenant-b")
                .Options,
            null!))
        {
            seed.Products.Add(foreignProduct);
            await seed.SaveChangesAsync();
        }

        HttpResponseMessage response = await Client.GetAsync($"/products/{foreignProduct.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListProducts_AfterCreate_IncludesProduct()
    {
        await Client.PostAsJsonAsync("/products", new
        {
            Name = "Listed", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "LIST-1", SellPriceAmount = 1m, SellPriceCurrency = "USD",
        });

        var list = await Client.GetFromJsonAsync<List<ProductSummaryDto>>("/products");

        Assert.NotNull(list);
        Assert.Contains(list!, p => p.Name == "Listed");
    }

    [Fact]
    public async Task CreateCategory_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/categories", new
        {
            Name = "Hardware", Slug = "hardware", ParentId = (Guid?)null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var category = await response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.Equal("hardware", category!.Slug);
    }

    [Fact]
    public async Task AddVariant_ToExistingProduct_ReturnsCreated()
    {
        var created = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Shirt", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "SHIRT", SellPriceAmount = 20m, SellPriceCurrency = "USD",
        });
        var product = await created.Content.ReadFromJsonAsync<ProductDto>();

        var response = await Client.PostAsJsonAsync($"/products/{product!.Id}/variants", new
        {
            ProductId = product.Id,
            Sku = "SHIRT-RED-L",
            SellPriceAmount = 22m,
            SellPriceCurrency = "USD",
            Attributes = new[] { new { Name = "Color", Value = "Red" }, new { Name = "Size", Value = "L" } },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var variant = await response.Content.ReadFromJsonAsync<VariantDto>();
        Assert.Equal("SHIRT-RED-L", variant!.Sku);
    }

    [Fact]
    public async Task UpdateSellPrice_ChangesDefaultVariantPrice()
    {
        var created = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Priced", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "PRICED-1", SellPriceAmount = 10m, SellPriceCurrency = "USD",
        });
        var product = await created.Content.ReadFromJsonAsync<ProductDto>();
        var variantId = product!.Variants[0].Id;

        var updated = await Client.PutAsJsonAsync(
            $"/products/{product.Id}/variants/{variantId}/sell-price",
            new { ProductId = product.Id, VariantId = variantId, Amount = 15m, Currency = "USD" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var reFetched = await Client.GetFromJsonAsync<ProductDto>($"/products/{product.Id}");
        Assert.Equal(15m, reFetched!.Variants[0].SellPriceAmount);
    }
}

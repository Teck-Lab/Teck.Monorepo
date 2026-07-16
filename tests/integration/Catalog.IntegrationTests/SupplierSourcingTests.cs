// <copyright file="SupplierSourcingTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Catalog.Application.Products.Responses;
using Catalog.Application.Suppliers.Responses;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Catalog.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class SupplierSourcingTests : CatalogIntegrationTestBase
{
    public SupplierSourcingTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CreateSupplier_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/suppliers", new
        {
            Name = "Acme", ContactEmail = "sales@acme.test", ContactPhone = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var supplier = await response.Content.ReadFromJsonAsync<SupplierDto>();
        Assert.Equal("Acme", supplier!.Name);
    }

    [Fact]
    public async Task GetSupplier_AfterCreate_ReturnsSupplier()
    {
        var created = await Client.PostAsJsonAsync("/suppliers", new
        {
            Name = "Globex", ContactEmail = (string?)null, ContactPhone = (string?)null,
        });
        var supplier = await created.Content.ReadFromJsonAsync<SupplierDto>();

        var fetched = await Client.GetFromJsonAsync<SupplierDto>($"/suppliers/{supplier!.Id}");

        Assert.Equal(supplier.Id, fetched!.Id);
        Assert.Equal("Globex", fetched.Name);
    }

    [Fact]
    public async Task UpdateSupplierCost_WritesHistoryRow()
    {
        var createdProduct = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Sourced", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "SRC-1", SellPriceAmount = 12m, SellPriceCurrency = "USD",
        });
        var product = await createdProduct.Content.ReadFromJsonAsync<ProductDto>();
        var variantId = product!.Variants[0].Id;

        var createdSupplier = await Client.PostAsJsonAsync("/suppliers", new
        {
            Name = "CostCo", ContactEmail = (string?)null, ContactPhone = (string?)null,
        });
        var supplier = await createdSupplier.Content.ReadFromJsonAsync<SupplierDto>();

        var link = await Client.PostAsJsonAsync($"/variants/{variantId}/suppliers", new
        {
            VariantId = variantId, SupplierId = supplier!.Id, CostAmount = 4m, CostCurrency = "USD",
            SupplierSku = "CC-1", LeadTimeDays = 5, MinOrderQuantity = 1, IsPreferred = true,
        });
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);

        var updated = await Client.PutAsJsonAsync(
            $"/variants/{variantId}/suppliers/{supplier.Id}/cost",
            new { VariantId = variantId, SupplierId = supplier.Id, CostAmount = 5m, CostCurrency = "USD" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var history = await Client.GetFromJsonAsync<List<SupplierPriceHistoryDto>>(
            $"/variants/{variantId}/suppliers/{supplier.Id}/history");
        Assert.NotNull(history);
        Assert.NotEmpty(history!);
    }
}

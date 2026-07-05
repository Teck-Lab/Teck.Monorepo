// <copyright file="ErrorPathTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Pricing.Application.Pricing.Responses;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Pricing.IntegrationTests;

/// <summary>End-to-end regression tests for previously-500ing edge cases: soft-deleted exchange rate re-add and non-ascending price tiers.</summary>
[Collection("SharedTestcontainers")]
public sealed class ErrorPathTests : PricingIntegrationTestBase
{
    /// <summary>Initializes a new instance of the <see cref="ErrorPathTests"/> class.</summary>
    /// <param name="fixture">The shared Testcontainers fixture.</param>
    public ErrorPathTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>Re-adding a rate for a currency pair whose prior rate was soft-deleted succeeds, because the unique index is filtered on <c>IsDeleted = false</c>.</summary>
    [Fact]
    public async Task SoftDeletedRate_CanBeReAdded()
    {
        var firstSet = await Client.PutAsJsonAsync("/exchange-rates", new
        {
            FromCurrency = "EUR",
            ToCurrency = "USD",
            Rate = 1.1m,
        });
        firstSet.EnsureSuccessStatusCode();

        var deleted = await Client.DeleteAsync(new Uri("/exchange-rates/EUR/USD", UriKind.Relative));
        deleted.EnsureSuccessStatusCode();

        var secondSet = await Client.PutAsJsonAsync("/exchange-rates", new
        {
            FromCurrency = "EUR",
            ToCurrency = "USD",
            Rate = 1.2m,
        });

        Assert.True(secondSet.IsSuccessStatusCode, $"Expected success, got {(int)secondSet.StatusCode}: {await secondSet.Content.ReadAsStringAsync()}");
    }

    /// <summary>Adding a price with non-ascending quantity tiers is rejected at the validation edge with 400, instead of the domain throwing a 500.</summary>
    [Fact]
    public async Task AddPrice_WithNonAscendingTiers_Returns400()
    {
        var productId = Guid.NewGuid();

        var created = await Client.PostAsJsonAsync("/price-lists", new
        {
            Name = "Non-ascending tiers",
            Currency = "USD",
        });
        created.EnsureSuccessStatusCode();
        var list = await created.Content.ReadFromJsonAsync<PriceListDto>();

        var priced = await Client.PutAsJsonAsync($"/price-lists/{list!.Id}/prices/{productId}", new
        {
            Id = list.Id,
            ProductId = productId,
            Amount = 10m,
            Tiers = new[]
            {
                new { MinQuantity = 10, Amount = 8m },
                new { MinQuantity = 5, Amount = 9m },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, priced.StatusCode);
    }
}

// <copyright file="OrderPlacedCrossServiceTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Orders.Domain.Entities;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace CrossService.IntegrationTests;

/// <summary>
/// Proves the supported platform-priced basket checkout path delivers its resulting order
/// placement over RabbitMQ to Inventory and commits the stock reservation.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class OrderPlacedCrossServiceTests(SharedTestcontainersFixture fixture)
{
    /// <summary>
    /// Registers stock, checks out through the supported basket ingress, and verifies the
    /// OrderPlaced V2 delivery reserves the expected inventory quantity.
    /// </summary>
    [Fact]
    public async Task PlatformPricedBasketCheckout_ReservesStockInInventory_AcrossRabbitMq()
    {
        using var harness = new ProductionLifecycleHarness(fixture);
        var product = await harness.CreateProductAsync(sellPrice: 10m);
        await harness.RegisterStockAsync(product.Id, quantity: 5);
        Assert.Equal(5, await harness.AvailabilityAsync(product.Id));

        Guid basketId = await harness.CheckoutAsync(product.Id, authorizedAmount: 25m);
        Order? order = await harness.WaitForOrderAsync(basketId, TimeSpan.FromSeconds(45));

        Assert.NotNull(order);
        Assert.Equal(20m, order!.Total);
        Assert.True(await harness.WaitForReservedAvailabilityAsync(product.Id));
        Assert.Equal(3, await harness.AvailabilityAsync(product.Id));
    }
}

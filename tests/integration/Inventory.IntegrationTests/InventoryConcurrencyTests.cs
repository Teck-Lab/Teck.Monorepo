// <copyright file="InventoryConcurrencyTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net.Http.Json;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Inventories.Host.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace Inventories.IntegrationTests;

/// <summary>
/// Headline correctness test: two <see cref="OrderPlacedIntegrationEvent"/>s racing for the same
/// single unit of stock must never both win. Proves that the <c>xmin</c> optimistic-concurrency
/// reload-retry in <c>ReservationCommitter</c> (fresh <see cref="IServiceScope"/> per retry, see
/// Task 15) prevents a double reserve, and exercises the <c>Reservation</c>/owned
/// <c>ReservationLine</c>/JSON-<c>Allocations</c> persistence path against real Postgres.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class InventoryConcurrencyTests : InventoryIntegrationTestBase
{
    /// <summary>Initializes a new instance of the <see cref="InventoryConcurrencyTests"/> class.</summary>
    /// <param name="fixture">The shared Postgres/RabbitMQ Testcontainers fixture.</param>
    public InventoryConcurrencyTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ConcurrentOrderPlaced_ForSingleUnitOfStock_NeverOversells()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        // Seed exactly one unit of stock, no backorder allowed: at most one of the two competing
        // orders below can legitimately win.
        var registerResponse = await Client.PostAsJsonAsync(
            "/inventory/stock-items",
            new
            {
                ProductId = productId,
                LocationId = locationId,
                QuantityOnHand = 1,
                AllowBackorder = false,
                ReorderThreshold = 0,
            });
        var registerBody = await registerResponse.Content.ReadAsStringAsync();
        Assert.True(registerResponse.IsSuccessStatusCode, $"POST /inventory/stock-items failed: {(int)registerResponse.StatusCode} {registerBody}");

        var registered = await registerResponse.Content.ReadFromJsonAsync<StockItemDto>();
        Assert.NotNull(registered);
        Assert.Equal(1, registered!.OnHand);
        Assert.Equal(1, registered.Available);

        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();

        // TenantId matches whatever RegisterStockItemHandler stamped on the seeded StockItem: the
        // test host has no multi-tenant strategy/store configured, so ITenantInfo.Id resolves to
        // string.Empty for every request/message in this process (see TenantDetails.Id default).
        OrderPlacedIntegrationEvent BuildEvent(Guid orderId) => new()
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            TenantId = string.Empty,
            Status = "Placed",
            Total = 10m,
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = [new OrderPlacedLine(productId, "Contested Product", quantity: 1, unitPrice: 10m, total: 10m)],
        };

        // Two independent DI scopes -> two independent IMessageBus instances -> two independent
        // (scoped) InventoryDbContext instances for the ambient attempt, so the two invocations
        // race as genuinely separate commits against the same StockItem row rather than sharing
        // one change tracker.
        using IServiceScope scope1 = Services.CreateScope();
        using IServiceScope scope2 = Services.CreateScope();
        IMessageBus bus1 = scope1.ServiceProvider.GetRequiredService<IMessageBus>();
        IMessageBus bus2 = scope2.ServiceProvider.GetRequiredService<IMessageBus>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Task.WhenAll(
            bus1.InvokeAsync(BuildEvent(orderId1), cts.Token),
            bus2.InvokeAsync(BuildEvent(orderId2), cts.Token));

        // Never oversold: the raw stored counters on the StockItem must show exactly one unit
        // reserved and zero available, no matter which order won.
        var stockItems = await Client.GetFromJsonAsync<IReadOnlyList<StockItemDto>>($"/inventory/stock-items?productId={productId}");
        Assert.NotNull(stockItems);
        StockItemDto stockItem = Assert.Single(stockItems!);
        Assert.Equal(1, stockItem.Reserved);
        Assert.Equal(0, stockItem.Available);

        // Effective availability (computed live from committed/held Reservation allocations,
        // GetAvailabilityHandler) must agree: this only holds if the winning Reservation's owned
        // ReservationLine + JSON Allocations column actually round-tripped through Postgres.
        var availability = await Client.GetFromJsonAsync<AvailabilityDto>($"/inventory/availability?productId={productId}");
        Assert.NotNull(availability);
        Assert.Equal(0, availability!.Available);

        // Exactly one of the two orders produced a Committed reservation; the other produced none.
        using IServiceScope readScope = Services.CreateScope();
        InventoryReadDbContext readDb = readScope.ServiceProvider.GetRequiredService<InventoryReadDbContext>();
        List<Reservation> reservationsForOrders = await readDb.Reservations
            .Where(r => r.SourceType == ReservationSource.Order && (r.SourceId == orderId1 || r.SourceId == orderId2))
            .ToListAsync(cts.Token);

        Assert.Single(reservationsForOrders);
        Reservation committed = Assert.Single(reservationsForOrders, r => r.Status == ReservationStatus.Committed);
        Assert.True(committed.SourceId == orderId1 || committed.SourceId == orderId2);

        // The winning Reservation must carry a real, persisted owned ReservationLine with a
        // non-empty JSON-backed Allocations collection covering the single reserved unit.
        var line = Assert.Single(committed.Lines);
        Assert.Equal(productId, line.ProductId);
        Assert.Equal(1, line.RequestedQuantity);
        Assert.Equal(0, line.BackorderedQuantity);
        var allocation = Assert.Single(line.Allocations);
        Assert.Equal(locationId, allocation.LocationId);
        Assert.Equal(1, allocation.Quantity);
    }
}

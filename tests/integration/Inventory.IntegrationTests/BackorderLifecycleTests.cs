// <copyright file="BackorderLifecycleTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Inventories.Application.Database;
using Inventories.Application.Inventory.Features.ExpireHeldReservations.V1;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Inventories.IntegrationTests;

/// <summary>Verifies the live inventory schema exposes bounded-backorder state.</summary>
[Collection("SharedTestcontainers")]
public sealed class BackorderLifecycleTests : InventoryIntegrationTestBase
{
    /// <summary>Initializes a new instance of the <see cref="BackorderLifecycleTests"/> class.</summary>
    public BackorderLifecycleTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>Ensures the committed migration creates the deadline and correlated-basket columns.</summary>
    [Fact]
    public void Model_Reservation_ContainsBoundedBackorderColumns()
    {
        using IServiceScope scope = Services.CreateScope();
        InventoryDbContext db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var entity = db.Model.FindEntityType(typeof(Inventories.Domain.Entities.Reservation));

        Assert.NotNull(entity);
        Assert.NotNull(entity!.FindProperty("BackorderExpiresAt"));
        Assert.NotNull(entity.FindProperty("BasketId"));
        Assert.NotNull(entity.FindProperty("BackorderReadyOutcomeKey"));
    }

    [Fact]
    public async Task PositiveAdjustment_RacingBackorderExpiry_LeavesOneTerminalStateAndOneLifecycleOutcome()
    {
        const string tenantId = "tenant-fill-expiry";
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var stock = StockItem.Create(productId, locationId, tenantId, 0, allowBackorder: true, reorderThreshold: -1);
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            tenantId,
            [new ReservationLine(productId, 1, 1, [])],
            now.AddSeconds(-1),
            Guid.NewGuid(),
            "fill-expiry-race");
        await InventoryHandlerHarness.SeedAsync(Services, stock, reservation);

        var outcomes = LifecycleOutcomeRecorder.Create(async message =>
        {
            if (message is BackorderReadyIntegrationEvent or BackorderExpiredIntegrationEvent)
            {
                Reservation persisted = await InventoryHandlerHarness.ReadReservationAsync(Services, tenantId, reservation.Id);
                Assert.True(
                    (persisted.Status == ReservationStatus.Committed && !persisted.HasOutstandingBackorder)
                    || persisted.Status == ReservationStatus.Expired,
                    "A lifecycle outcome must be published only after its terminal reservation state is committed.");
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<int> expiry = InventoryHandlerHarness.ExpireAsync(Services, tenantId, new FixedTimeProvider(now), outcomes.Bus, cts.Token);
        Task adjust = InventoryHandlerHarness.AdjustAsync(Services, tenantId, stock.Id, 1, outcomes.Bus, cts.Token);
        await Task.WhenAll(expiry, adjust);

        Reservation finalReservation = await InventoryHandlerHarness.ReadReservationAsync(Services, tenantId, reservation.Id);
        StockItem finalStock = Assert.Single(await InventoryHandlerHarness.ReadStockAsync(Services, tenantId, stock.Id));
        object lifecycleOutcome = Assert.Single(outcomes.Messages.Where(message =>
            message is BackorderReadyIntegrationEvent or BackorderExpiredIntegrationEvent));
        Assert.Equal(1, finalStock.QuantityOnHand);
        Assert.True(
            (finalReservation.Status == ReservationStatus.Committed
             && !finalReservation.HasOutstandingBackorder
             && finalStock.QuantityReserved == 1
             && lifecycleOutcome is BackorderReadyIntegrationEvent ready
             && ready.IdempotencyKey == $"backorder-ready:{reservation.Id:N}")
            || (finalReservation.Status == ReservationStatus.Expired
                && finalStock.QuantityReserved == 0
                && lifecycleOutcome is BackorderExpiredIntegrationEvent expired
                && expired.IdempotencyKey == $"backorder-expired:{reservation.Id:N}"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

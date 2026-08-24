// <copyright file="ReleaseReservationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Inventories.Application.Database;
using Inventories.Application.Inventory.Features.ReleaseReservation.V1;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Inventories.IntegrationTests;

/// <summary>Verifies release correlation is indexed in the persisted inventory model.</summary>
[Collection("SharedTestcontainers")]
public sealed class ReleaseReservationTests : InventoryIntegrationTestBase
{
    /// <summary>Initializes a new instance of the <see cref="ReleaseReservationTests"/> class.</summary>
    public ReleaseReservationTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>Ensures tenant-scoped correlation lookup is available for idempotent release.</summary>
    [Fact]
    public void Model_Reservation_IndexesTenantCorrelation()
    {
        using IServiceScope scope = Services.CreateScope();
        InventoryDbContext db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var entity = db.Model.FindEntityType(typeof(Inventories.Domain.Entities.Reservation));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["TenantId", "SourceCorrelationId"]));
    }

    [Fact]
    public async Task ConcurrentAndReplayedRelease_ReleasesOnceAndPublishesOneOutcomeAfterCommit()
    {
        const string tenantId = "tenant-release";
        const string correlationId = "release-race";
        const string requestId = "release-request";
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var stock = StockItem.Create(productId, locationId, tenantId, 1, allowBackorder: false, reorderThreshold: -1);
        stock.Reserve(1);
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            orderId,
            tenantId,
            [new ReservationLine(productId, 1, 0, [new Allocation(locationId, 1)])],
            basketId: Guid.NewGuid(),
            sourceCorrelationId: correlationId);
        await InventoryHandlerHarness.SeedAsync(Services, stock, reservation);
        var command = new ReleaseReservationCommand(orderId, reservation.BasketId, tenantId, correlationId, requestId);
        var outcomes = LifecycleOutcomeRecorder.Create(async message =>
        {
            if (message is StockReleasedIntegrationEvent)
            {
                Reservation persistedReservation = await InventoryHandlerHarness.ReadReservationAsync(Services, reservation.Id);
                StockItem persistedStock = Assert.Single(await InventoryHandlerHarness.ReadStockAsync(Services, stock.Id));
                Assert.Equal(ReservationStatus.Released, persistedReservation.Status);
                Assert.Equal(0, persistedStock.QuantityReserved);
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Task.WhenAll(
            InventoryHandlerHarness.ReleaseAsync(Services, command, outcomes.Bus, cts.Token),
            InventoryHandlerHarness.ReleaseAsync(Services, command, outcomes.Bus, cts.Token));
        await InventoryHandlerHarness.ReleaseAsync(Services, command, outcomes.Bus, cts.Token);

        Reservation finalReservation = await InventoryHandlerHarness.ReadReservationAsync(Services, reservation.Id);
        StockItem finalStock = Assert.Single(await InventoryHandlerHarness.ReadStockAsync(Services, stock.Id));
        StockReleasedIntegrationEvent released = Assert.Single(outcomes.Messages.OfType<StockReleasedIntegrationEvent>());
        Assert.Equal(ReservationStatus.Released, finalReservation.Status);
        Assert.Equal(0, finalStock.QuantityReserved);
        Assert.Equal(requestId, released.RequestId);
        Assert.Equal(correlationId, released.SourceCorrelationId);
    }

    [Fact]
    public async Task ReleaseRacingBackorderExpiry_LeavesOneTerminalStateAndOneKeyedOutcome()
    {
        const string tenantId = "tenant-release-expiry";
        const string correlationId = "release-expiry-race";
        const string requestId = "release-expiry-request";
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var stock = StockItem.Create(productId, locationId, tenantId, 1, allowBackorder: true, reorderThreshold: -1);
        stock.Reserve(1);
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            orderId,
            tenantId,
            [new ReservationLine(productId, 2, 1, [new Allocation(locationId, 1)])],
            now.AddSeconds(-1),
            Guid.NewGuid(),
            correlationId);
        await InventoryHandlerHarness.SeedAsync(Services, stock, reservation);
        var command = new ReleaseReservationCommand(orderId, reservation.BasketId, tenantId, correlationId, requestId);
        var outcomes = LifecycleOutcomeRecorder.Create(async message =>
        {
            if (message is StockReleasedIntegrationEvent or BackorderExpiredIntegrationEvent)
            {
                Reservation persistedReservation = await InventoryHandlerHarness.ReadReservationAsync(Services, reservation.Id);
                StockItem persistedStock = Assert.Single(await InventoryHandlerHarness.ReadStockAsync(Services, stock.Id));
                Assert.True(persistedReservation.Status == ReservationStatus.Released || persistedReservation.Status == ReservationStatus.Expired);
                Assert.Equal(0, persistedStock.QuantityReserved);
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Task.WhenAll(
            InventoryHandlerHarness.ReleaseAsync(Services, command, outcomes.Bus, cts.Token),
            InventoryHandlerHarness.ExpireAsync(Services, new FixedTimeProvider(now), outcomes.Bus, cts.Token));

        Reservation finalReservation = await InventoryHandlerHarness.ReadReservationAsync(Services, reservation.Id);
        StockItem finalStock = Assert.Single(await InventoryHandlerHarness.ReadStockAsync(Services, stock.Id));
        object lifecycleOutcome = Assert.Single(outcomes.Messages.Where(message =>
            message is StockReleasedIntegrationEvent or BackorderExpiredIntegrationEvent));
        Assert.Equal(0, finalStock.QuantityReserved);
        Assert.True(
            (finalReservation.Status == ReservationStatus.Released
             && lifecycleOutcome is StockReleasedIntegrationEvent released
             && released.RequestId == requestId)
            || (finalReservation.Status == ReservationStatus.Expired
                && lifecycleOutcome is BackorderExpiredIntegrationEvent expired
                && expired.IdempotencyKey == $"backorder-expired:{reservation.Id:N}"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

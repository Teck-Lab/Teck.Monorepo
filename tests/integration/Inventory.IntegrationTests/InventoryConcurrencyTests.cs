// <copyright file="InventoryConcurrencyTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Reflection;
using Inventories.Application.Database;
using Inventories.Application.Inventory;
using Inventories.Application.Inventory.EventHandlers.IntegrationEvents;
using Inventories.Application.Inventory.Features.AdjustStock.V1;
using Inventories.Application.Inventory.Features.ExpireHeldReservations.V1;
using Inventories.Application.Inventory.Features.ReleaseReservation.V1;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Inventories.Host.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.FeatureFlags;
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

    [Fact]
    public async Task SequentialV1ThenV2OrderPlacement_UsesOnePostgresReservationAndPersistsLifecycleHandoff()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var response = await Client.PostAsJsonAsync(
            "/inventory/stock-items",
            new { ProductId = productId, LocationId = locationId, QuantityOnHand = 2, AllowBackorder = false, ReorderThreshold = 0 });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var v1 = new OrderPlacedIntegrationEvent
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            TenantId = string.Empty,
            Status = "Placed",
            Total = 10m,
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = [new OrderPlacedLine(productId, "Widget", 2, 5m, 10m)],
        };
        var v2 = new OrderPlacedV2IntegrationEvent
        {
            OrderId = orderId,
            BasketId = basketId,
            TenantId = string.Empty,
            SourceCorrelationId = "checkout-handoff",
            Lines = [new OrderPlacedLine(productId, "Widget", 2, 5m, 10m)],
        };

        using IServiceScope firstScope = Services.CreateScope();
        using IServiceScope secondScope = Services.CreateScope();
        await firstScope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(v1);
        await OrderPlacedV2Handler.Handle(
            v2,
            secondScope.ServiceProvider.GetRequiredService<SharedKernel.Core.Database.IGenericWriteRepository<StockItem, Guid>>(),
            secondScope.ServiceProvider.GetRequiredService<SharedKernel.Core.Database.IGenericWriteRepository<Reservation, Guid>>(),
            secondScope.ServiceProvider.GetRequiredService<SharedKernel.Core.Database.IGenericReadRepository<LocationPriority, Guid>>(),
            secondScope.ServiceProvider.GetRequiredService<SharedKernel.Core.Database.IUnitOfWork>(),
            secondScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            secondScope.ServiceProvider.GetRequiredService<IOptions<InventoryOptions>>(),
            secondScope.ServiceProvider.GetRequiredService<TimeProvider>(),
            secondScope.ServiceProvider.GetRequiredService<IFeatureProvider>(),
            secondScope.ServiceProvider.GetRequiredService<IMessageBus>(),
            CancellationToken.None);

        using IServiceScope readScope = Services.CreateScope();
        InventoryReadDbContext db = readScope.ServiceProvider.GetRequiredService<InventoryReadDbContext>();
        Reservation reservation = Assert.Single(await db.Reservations
            .Where(candidate => candidate.TenantId == string.Empty && candidate.SourceType == ReservationSource.Order && candidate.SourceId == orderId)
            .ToListAsync());
        Assert.Equal(basketId, reservation.BasketId);
        Assert.Equal("checkout-handoff", reservation.SourceCorrelationId);
        Assert.True(reservation.IsLifecycleV2);
        Assert.Equal(2, Assert.Single(reservation.Lines).Allocations.Single().Quantity);
    }

    [Fact]
    public async Task ConcurrentV1AndV2OrderPlacement_UsesOneReservationAndOneV2LifecycleOutcome()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var response = await Client.PostAsJsonAsync(
            "/inventory/stock-items",
            new { ProductId = productId, LocationId = locationId, QuantityOnHand = 2, AllowBackorder = false, ReorderThreshold = 0 });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var v1 = new OrderPlacedIntegrationEvent
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            TenantId = string.Empty,
            Status = "Placed",
            Total = 10m,
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = [new OrderPlacedLine(productId, "Widget", 2, 5m, 10m)],
        };
        var v2 = new OrderPlacedV2IntegrationEvent
        {
            OrderId = orderId,
            BasketId = basketId,
            TenantId = string.Empty,
            SourceCorrelationId = "checkout-concurrent-handoff",
            Lines = [new OrderPlacedLine(productId, "Widget", 2, 5m, 10m)],
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var outcomes = LifecycleOutcomeRecorder.Create(async message =>
        {
            if (message is StockReservedV2IntegrationEvent)
            {
                using IServiceScope publishScope = Services.CreateScope();
                InventoryReadDbContext publishDb = publishScope.ServiceProvider.GetRequiredService<InventoryReadDbContext>();
                Reservation persisted = await publishDb.Reservations
                    .SingleAsync(candidate => candidate.TenantId == string.Empty && candidate.SourceType == ReservationSource.Order && candidate.SourceId == orderId, cts.Token)
                    .ConfigureAwait(false);
                Assert.True(persisted.IsLifecycleV2);
                Assert.Equal(basketId, persisted.BasketId);
                Assert.Equal("checkout-concurrent-handoff", persisted.SourceCorrelationId);
            }
        });

        await Task.WhenAll(
            InventoryHandlerHarness.PlaceV1Async(Services, v1, outcomes.Bus, cts.Token),
            InventoryHandlerHarness.PlaceV2Async(Services, v2, outcomes.Bus, cts.Token));

        using IServiceScope readScope = Services.CreateScope();
        InventoryReadDbContext db = readScope.ServiceProvider.GetRequiredService<InventoryReadDbContext>();
        Reservation reservation = Assert.Single(await db.Reservations
            .Where(candidate => candidate.TenantId == string.Empty && candidate.SourceType == ReservationSource.Order && candidate.SourceId == orderId)
            .ToListAsync(cts.Token));
        Assert.True(reservation.IsLifecycleV2);
        Assert.Equal(basketId, reservation.BasketId);
        Assert.Equal("checkout-concurrent-handoff", reservation.SourceCorrelationId);
        Assert.Equal(2, Assert.Single(reservation.Lines).Allocations.Single().Quantity);

        StockItem stock = Assert.Single(await db.StockItems.Where(item => item.Id != Guid.Empty && item.TenantId == string.Empty && item.ProductId == productId).ToListAsync(cts.Token));
        Assert.Equal(2, stock.QuantityReserved);
        Assert.Equal(0, stock.Available);

        StockReservedIntegrationEvent v1Outcome = Assert.Single(outcomes.Messages.OfType<StockReservedIntegrationEvent>());
        Assert.Equal(reservation.Id, v1Outcome.ReservationId);
        Assert.Equal(orderId, v1Outcome.SourceId);
        StockReservedV2IntegrationEvent lifecycleOutcome = Assert.Single(outcomes.Messages.OfType<StockReservedV2IntegrationEvent>());
        Assert.Equal(reservation.Id, lifecycleOutcome.ReservationId);
        Assert.Equal(basketId, lifecycleOutcome.BasketId);
        Assert.Equal("checkout-concurrent-handoff", lifecycleOutcome.SourceCorrelationId);
        Assert.Equal($"stock-reserved:{orderId:N}", lifecycleOutcome.IdempotencyKey);
    }

    [Fact]
    public async Task ConcurrentPositiveAdjustments_AtDistinctLocations_FillOneBackorderAndPublishOneReadyOutcomeAfterCommit()
    {
        const string tenantId = "tenant-concurrent-adjust";
        var productId = Guid.NewGuid();
        var firstLocationId = Guid.NewGuid();
        var secondLocationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var first = StockItem.Create(productId, firstLocationId, tenantId, 0, allowBackorder: true, reorderThreshold: -1);
        var second = StockItem.Create(productId, secondLocationId, tenantId, 0, allowBackorder: true, reorderThreshold: -1);
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            orderId,
            tenantId,
            [new ReservationLine(productId, RequestedQuantity: 2, BackorderedQuantity: 2, Allocations: [])],
            now.AddMinutes(5),
            basketId,
            "adjust-race");
        await InventoryHandlerHarness.SeedAsync(Services, first, second, reservation);

        var outcomes = LifecycleOutcomeRecorder.Create(async message =>
        {
            if (message is BackorderReadyIntegrationEvent)
            {
                Reservation persisted = await InventoryHandlerHarness.ReadReservationAsync(Services, reservation.Id);
                Assert.Equal(ReservationStatus.Committed, persisted.Status);
                Assert.False(persisted.HasOutstandingBackorder);
                Assert.Null(persisted.BackorderExpiresAt);
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Task.WhenAll(
            InventoryHandlerHarness.AdjustAsync(Services, first.Id, 1, outcomes.Bus, cts.Token),
            InventoryHandlerHarness.AdjustAsync(Services, second.Id, 1, outcomes.Bus, cts.Token));

        IReadOnlyList<StockItem> stock = await InventoryHandlerHarness.ReadStockAsync(Services, first.Id, second.Id);
        Assert.All(stock, item =>
        {
            Assert.Equal(1, item.QuantityOnHand);
            Assert.Equal(1, item.QuantityReserved);
            Assert.Equal(0, item.Available);
        });
        Reservation finalReservation = await InventoryHandlerHarness.ReadReservationAsync(Services, reservation.Id);
        ReservationLine line = Assert.Single(finalReservation.Lines);
        Assert.Equal(0, line.BackorderedQuantity);
        Assert.Equal(2, line.Allocations.Sum(allocation => allocation.Quantity));
        Assert.Equal(new[] { firstLocationId, secondLocationId }.OrderBy(id => id), line.Allocations.Select(allocation => allocation.LocationId).OrderBy(id => id));
        Assert.True(finalReservation.IsLifecycleV2);
        Assert.NotNull(finalReservation.BackorderReadyOutcomeKey);
        BackorderReadyIntegrationEvent ready = Assert.Single(outcomes.Messages.OfType<BackorderReadyIntegrationEvent>());
        Assert.Equal($"backorder-ready:{reservation.Id:N}", ready.IdempotencyKey);
        Assert.Equal(1, outcomes.Messages.OfType<BackorderReadyIntegrationEvent>().Count());
    }

    [Fact]
    public async Task PositiveAdjustment_ForOneTenant_DoesNotMutateAnotherTenantsSameProductBackorder()
    {
        const string firstTenant = "tenant-a";
        const string secondTenant = "tenant-b";
        var productId = Guid.NewGuid();
        var firstLocationId = Guid.NewGuid();
        var secondLocationId = Guid.NewGuid();
        var firstStock = StockItem.Create(productId, firstLocationId, firstTenant, 0, allowBackorder: true, reorderThreshold: -1);
        var secondStock = StockItem.Create(productId, secondLocationId, secondTenant, 0, allowBackorder: true, reorderThreshold: -1);
        var firstReservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            firstTenant,
            [new ReservationLine(productId, 1, 1, [])],
            DateTimeOffset.UtcNow.AddMinutes(5),
            Guid.NewGuid(),
            "tenant-a-backorder");
        var secondReservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            secondTenant,
            [new ReservationLine(productId, 1, 1, [])],
            DateTimeOffset.UtcNow.AddMinutes(5),
            Guid.NewGuid(),
            "tenant-b-backorder");
        await InventoryHandlerHarness.SeedAsync(Services, firstStock, secondStock, firstReservation, secondReservation);
        var outcomes = LifecycleOutcomeRecorder.Create();

        await InventoryHandlerHarness.AdjustAsync(Services, firstStock.Id, 1, outcomes.Bus, CancellationToken.None);

        Reservation persistedFirst = await InventoryHandlerHarness.ReadReservationAsync(Services, firstReservation.Id);
        Reservation persistedSecond = await InventoryHandlerHarness.ReadReservationAsync(Services, secondReservation.Id);
        StockItem persistedSecondStock = Assert.Single(await InventoryHandlerHarness.ReadStockAsync(Services, secondStock.Id));
        Assert.Equal(0, Assert.Single(persistedFirst.Lines).BackorderedQuantity);
        Assert.Equal(1, Assert.Single(persistedSecond.Lines).BackorderedQuantity);
        Assert.Empty(Assert.Single(persistedSecond.Lines).Allocations);
        Assert.Equal(0, persistedSecondStock.QuantityOnHand);
        Assert.Equal(0, persistedSecondStock.QuantityReserved);
        BackorderReadyIntegrationEvent ready = Assert.Single(outcomes.Messages.OfType<BackorderReadyIntegrationEvent>());
        Assert.Equal(firstTenant, ready.TenantId);
        Assert.Equal($"backorder-ready:{firstReservation.Id:N}", ready.IdempotencyKey);
    }
}

internal static class InventoryHandlerHarness
{
    internal static async Task SeedAsync(IServiceProvider services, params object[] entities)
    {
        using IServiceScope scope = services.CreateScope();
        InventoryDbContext db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    internal static async Task AdjustAsync(IServiceProvider services, Guid stockItemId, int delta, IMessageBus bus, CancellationToken ct)
    {
        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;
        IFeatureProvider featureProvider = LifecycleFeatureProvider.Enabled;
        await AdjustStockHandler.Handle(
            new AdjustStockCommand(stockItemId, delta),
            provider.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
            provider.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
            provider.GetRequiredService<IUnitOfWork>(),
            bus,
            provider.GetRequiredService<TimeProvider>(),
            ct,
            featureProvider,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<InventoryOptions>>());
    }

    internal static async Task PlaceV1Async(IServiceProvider services, OrderPlacedIntegrationEvent evt, IMessageBus bus, CancellationToken ct)
    {
        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;
        await OrderPlacedHandler.Handle(
            evt,
            provider.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
            provider.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
            provider.GetRequiredService<IGenericReadRepository<LocationPriority, Guid>>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<InventoryOptions>>(),
            bus,
            ct,
            provider.GetRequiredService<TimeProvider>());
    }

    internal static async Task PlaceV2Async(IServiceProvider services, OrderPlacedV2IntegrationEvent evt, IMessageBus bus, CancellationToken ct)
    {
        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;
        await OrderPlacedV2Handler.Handle(
            evt,
            provider.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
            provider.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
            provider.GetRequiredService<IGenericReadRepository<LocationPriority, Guid>>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<InventoryOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            LifecycleFeatureProvider.Enabled,
            bus,
            ct);
    }

    internal static async Task<int> ExpireAsync(IServiceProvider services, TimeProvider timeProvider, IMessageBus bus, CancellationToken ct)
    {
        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;
        return await ExpireHeldReservationsHandler.Handle(
            new ExpireHeldReservationsCommand(),
            provider.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
            provider.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
            provider.GetRequiredService<IUnitOfWork>(),
            timeProvider,
            ct,
            bus,
            LifecycleFeatureProvider.Enabled,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<InventoryOptions>>());
    }

    internal static async Task ReleaseAsync(IServiceProvider services, ReleaseReservationCommand command, IMessageBus bus, CancellationToken ct)
    {
        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;
        await ReleaseReservationHandler.Handle(
            command,
            provider.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
            provider.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
            provider.GetRequiredService<IUnitOfWork>(),
            bus,
            ct,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<InventoryOptions>>());
    }

    internal static async Task<IReadOnlyList<StockItem>> ReadStockAsync(IServiceProvider services, params Guid[] ids)
    {
        using IServiceScope scope = services.CreateScope();
        InventoryReadDbContext db = scope.ServiceProvider.GetRequiredService<InventoryReadDbContext>();
        return await db.StockItems.Where(item => ids.Contains(item.Id)).ToListAsync();
    }

    internal static async Task<Reservation> ReadReservationAsync(IServiceProvider services, Guid reservationId)
    {
        using IServiceScope scope = services.CreateScope();
        InventoryReadDbContext db = scope.ServiceProvider.GetRequiredService<InventoryReadDbContext>();
        return await db.Reservations.SingleAsync(reservation => reservation.Id == reservationId);
    }
}

internal class LifecycleOutcomeRecorder : DispatchProxy
{
    private readonly ConcurrentQueue<object> messages = new();
    private Func<object, Task>? onPublish;

    internal IMessageBus Bus { get; private set; } = null!;

    internal IReadOnlyCollection<object> Messages => messages.ToArray();

    internal static LifecycleOutcomeRecorder Create(Func<object, Task>? onPublish = null)
    {
        IMessageBus bus = Create<IMessageBus, LifecycleOutcomeRecorder>();
        var recorder = (LifecycleOutcomeRecorder)(object)bus;
        recorder.Bus = bus;
        recorder.onPublish = onPublish;
        return recorder;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "PublishAsync" && args is [object message, ..])
        {
            messages.Enqueue(message);
            Task published = ObservePublishAsync(message);
            return targetMethod.ReturnType == typeof(ValueTask) ? new ValueTask(published) : published;
        }

        throw new NotSupportedException($"The test recorder only supports PublishAsync, not {targetMethod?.Name}.");
    }

    private Task ObservePublishAsync(object message) => onPublish?.Invoke(message) ?? Task.CompletedTask;
}

internal sealed class LifecycleFeatureProvider : IFeatureProvider
{
    internal static readonly IFeatureProvider Enabled = new LifecycleFeatureProvider();

    public bool IsEnabled(string flagKey, bool defaultValue = false) => string.Equals(flagKey, "CheckoutLifecycleV2", StringComparison.OrdinalIgnoreCase);

    public void SetFlag(string flagKey, bool enabled)
    {
    }
}

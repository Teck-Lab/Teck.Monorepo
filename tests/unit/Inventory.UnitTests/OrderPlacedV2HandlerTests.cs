using Ardalis.Specification;
using Inventories.Application.Inventory;
using Inventories.Application.Inventory.EventHandlers.IntegrationEvents;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.FeatureFlags;
using Wolverine;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>Regression coverage for the order V1-to-V2 lifecycle handoff.</summary>
public sealed class OrderPlacedV2HandlerTests
{
    private static readonly object FeatureFlagEnvironmentLock = new();

    [Fact]
    public async Task Handle_WhenReservationContentionExhausts_ReleasesBasketInFreshScopeBeforePublishingRejection()
    {
        const string tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var ambientStock = StockItem.Create(productId, Guid.NewGuid(), tenantId, 2, false, -100);
        var poisonedReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        poisonedReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(null));
        var poisonedStockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        poisonedStockItems.ListAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>([ambientStock]));
        var poisonedUnitOfWork = Substitute.For<IUnitOfWork>();
        poisonedUnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateConcurrencyException()));

        var freshStock = StockItem.Create(productId, Guid.NewGuid(), tenantId, 2, false, -100);
        freshStock.Reserve(1);
        Reservation basketReservation = Reservation.CreateHeld(
            ReservationSource.Basket,
            basketId,
            tenantId,
            DateTimeOffset.UtcNow.AddMinutes(15),
            [new ReservationLine(productId, 1, 0, [new Allocation(freshStock.LocationId, 1)])]);
        var freshReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        freshReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<Reservation?>(null),
                Task.FromResult<Reservation?>(basketReservation));
        var freshStockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        freshStockItems.FirstOrDefaultAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StockItem?>(freshStock));
        var freshUnitOfWork = Substitute.For<IUnitOfWork>();
        var freshServices = new ServiceCollection()
            .AddSingleton(freshReservations)
            .AddSingleton(freshStockItems)
            .AddSingleton(freshUnitOfWork)
            .AddSingleton<IFeatureProvider>(FeatureProvider(enabled: false))
            .BuildServiceProvider();
        var freshScope = Substitute.For<IServiceScope>();
        freshScope.ServiceProvider.Returns(freshServices);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(freshScope);
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedV2Handler.Handle(
            V2Event(orderId, basketId, tenantId, productId),
            poisonedStockItems,
            poisonedReservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(),
            poisonedUnitOfWork,
            scopeFactory,
            Options.Create(new InventoryOptions { MaxReserveRetries = 0 }),
            TimeProvider.System,
            FeatureProvider(enabled: false),
            bus,
            CancellationToken.None);

        Assert.Equal(ReservationStatus.Released, basketReservation.Status);
        Assert.Equal(0, freshStock.QuantityReserved);
        await poisonedUnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await freshUnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservationRejectedIntegrationEvent>(published =>
            published.SourceId == orderId && published.TenantId == tenantId));
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedV2IntegrationEvent>());
        Received.InOrder(async () =>
        {
            await freshUnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ConfigureAwait(false);
            await bus.PublishAsync(Arg.Any<StockReservationRejectedIntegrationEvent>()).ConfigureAwait(false);
        });
    }

    [Fact]
    public async Task Handle_WhenAmbientSaveConflictsAndFreshRetryRejectsWithoutWinner_ReleasesBasketInFreshScopeBeforePublishingRejection()
    {
        const string tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var poisonedStock = StockItem.Create(productId, Guid.NewGuid(), tenantId, 2, false, -100);
        var poisonedReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        poisonedReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(null));
        var poisonedStockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        poisonedStockItems.ListAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>([poisonedStock]));
        var poisonedUnitOfWork = Substitute.For<IUnitOfWork>();
        poisonedUnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateConcurrencyException()));

        var retryReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        retryReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(null));
        var retryStockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        retryStockItems.ListAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>([StockItem.Create(productId, Guid.NewGuid(), tenantId, 0, false, -100)]));
        var retryServices = new ServiceCollection()
            .AddSingleton(retryReservations)
            .AddSingleton(retryStockItems)
            .AddSingleton(Substitute.For<IGenericReadRepository<LocationPriority, Guid>>())
            .AddSingleton(Substitute.For<IUnitOfWork>())
            .BuildServiceProvider();

        var winnerLookupReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        winnerLookupReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(null));
        var winnerLookupServices = new ServiceCollection()
            .AddSingleton(winnerLookupReservations)
            .AddSingleton(Substitute.For<IUnitOfWork>())
            .BuildServiceProvider();

        var releasedStock = StockItem.Create(productId, Guid.NewGuid(), tenantId, 2, false, -100);
        releasedStock.Reserve(1);
        Reservation basketReservation = Reservation.CreateHeld(
            ReservationSource.Basket,
            basketId,
            tenantId,
            DateTimeOffset.UtcNow.AddMinutes(15),
            [new ReservationLine(productId, 1, 0, [new Allocation(releasedStock.LocationId, 1)])]);
        var releaseReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        releaseReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(null), Task.FromResult<Reservation?>(basketReservation));
        var releaseStockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        releaseStockItems.FirstOrDefaultAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StockItem?>(releasedStock));
        var releaseUnitOfWork = Substitute.For<IUnitOfWork>();
        var releaseServices = new ServiceCollection()
            .AddSingleton(releaseReservations)
            .AddSingleton(releaseStockItems)
            .AddSingleton(releaseUnitOfWork)
            .AddSingleton<IFeatureProvider>(FeatureProvider(enabled: false))
            .BuildServiceProvider();

        var retryScope = Substitute.For<IServiceScope>();
        retryScope.ServiceProvider.Returns(retryServices);
        var winnerLookupScope = Substitute.For<IServiceScope>();
        winnerLookupScope.ServiceProvider.Returns(winnerLookupServices);
        var releaseScope = Substitute.For<IServiceScope>();
        releaseScope.ServiceProvider.Returns(releaseServices);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(retryScope, winnerLookupScope, releaseScope);
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedV2Handler.Handle(
            V2Event(orderId, basketId, tenantId, productId),
            poisonedStockItems,
            poisonedReservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(),
            poisonedUnitOfWork,
            scopeFactory,
            Options.Create(new InventoryOptions { MaxReserveRetries = 1 }),
            TimeProvider.System,
            FeatureProvider(enabled: false),
            bus,
            CancellationToken.None);

        Assert.Equal(2, poisonedStock.QuantityReserved);
        Assert.Equal(ReservationStatus.Released, basketReservation.Status);
        Assert.Equal(0, releasedStock.QuantityReserved);
        await poisonedReservations.Received(1).FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>());
        await poisonedUnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await releaseUnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservationRejectedIntegrationEvent>(published =>
            published.SourceId == orderId && published.TenantId == tenantId));
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedV2IntegrationEvent>());
        Received.InOrder(async () =>
        {
            await releaseUnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ConfigureAwait(false);
            await bus.PublishAsync(Arg.Any<StockReservationRejectedIntegrationEvent>()).ConfigureAwait(false);
        });
    }

    [Fact]
    public async Task Handle_WhenGenericContentionExhaustsAndV1WinnerExists_AdoptsV2ProvenanceInFreshScope()
    {
        const string tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var ambientReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        ambientReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(null));
        var ambientStockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        ambientStockItems.ListAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>([StockItem.Create(productId, Guid.NewGuid(), tenantId, 2, false, -100)]));
        var poisonedUnitOfWork = Substitute.For<IUnitOfWork>();
        poisonedUnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateConcurrencyException()));

        Reservation v1Winner = Reservation.CreateCommitted(
            ReservationSource.Order,
            orderId,
            tenantId,
            [new ReservationLine(productId, 2, 0, [new Allocation(Guid.NewGuid(), 2)])]);
        var freshReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        freshReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(v1Winner));
        var freshUnitOfWork = Substitute.For<IUnitOfWork>();
        var freshServices = new ServiceCollection()
            .AddSingleton(freshReservations)
            .AddSingleton(freshUnitOfWork)
            .BuildServiceProvider();
        var freshScope = Substitute.For<IServiceScope>();
        freshScope.ServiceProvider.Returns(freshServices);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(freshScope);
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedV2Handler.Handle(
            V2Event(orderId, basketId, tenantId, productId),
            ambientStockItems,
            ambientReservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(),
            poisonedUnitOfWork,
            scopeFactory,
            Options.Create(new InventoryOptions { MaxReserveRetries = 0 }),
            TimeProvider.System,
            FeatureProvider(enabled: true),
            bus,
            CancellationToken.None);

        Assert.True(v1Winner.IsLifecycleV2);
        Assert.Equal(basketId, v1Winner.BasketId);
        Assert.Equal("checkout-correlation", v1Winner.SourceCorrelationId);
        await poisonedUnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await freshUnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservedV2IntegrationEvent>(published =>
            published.ReservationId == v1Winner.Id
            && published.BasketId == basketId
            && published.SourceCorrelationId == "checkout-correlation"
            && published.IdempotencyKey == $"stock-reserved:{orderId:N}"));
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedIntegrationEvent>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WhenOrdinaryRejectionFindsV1Winner_AdoptsV2ProvenanceInFreshScope()
    {
        const string tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var ambientReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        ambientReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(null));
        var ambientStockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        ambientStockItems.ListAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>([StockItem.Create(productId, Guid.NewGuid(), tenantId, 0, false, -100)]));
        var ambientUnitOfWork = Substitute.For<IUnitOfWork>();

        Reservation v1Winner = Reservation.CreateCommitted(
            ReservationSource.Order,
            orderId,
            tenantId,
            [new ReservationLine(productId, 2, 0, [new Allocation(Guid.NewGuid(), 2)])]);
        var freshReservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        freshReservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(v1Winner));
        var freshUnitOfWork = Substitute.For<IUnitOfWork>();
        var freshServices = new ServiceCollection()
            .AddSingleton(freshReservations)
            .AddSingleton(freshUnitOfWork)
            .BuildServiceProvider();
        var freshScope = Substitute.For<IServiceScope>();
        freshScope.ServiceProvider.Returns(freshServices);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(freshScope);
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedV2Handler.Handle(
            V2Event(orderId, basketId, tenantId, productId),
            ambientStockItems,
            ambientReservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(),
            ambientUnitOfWork,
            scopeFactory,
            Options.Create(new InventoryOptions { MaxReserveRetries = 0 }),
            TimeProvider.System,
            FeatureProvider(enabled: true),
            bus,
            CancellationToken.None);

        Assert.True(v1Winner.IsLifecycleV2);
        Assert.Equal(basketId, v1Winner.BasketId);
        Assert.Equal("checkout-correlation", v1Winner.SourceCorrelationId);
        await ambientUnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await freshUnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservedV2IntegrationEvent>(published =>
            published.ReservationId == v1Winner.Id
            && published.BasketId == basketId
            && published.SourceCorrelationId == "checkout-correlation"
            && published.IdempotencyKey == $"stock-reserved:{orderId:N}"));
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedIntegrationEvent>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedV2IntegrationEvent>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservedIntegrationEvent>());
    }

    [Fact]
    public void FeatureProvider_FrozenDeploymentValueControlsCheckoutLifecycleV2()
    {
        Assert.True(FeatureProvider(enabled: true).IsEnabled("CheckoutLifecycleV2"));
        Assert.False(FeatureProvider(enabled: false).IsEnabled("CheckoutLifecycleV2"));

        var services = new ServiceCollection();
        services.AddInventoryFeatureFlags(new ConfigurationBuilder().Build());
        Assert.False(services.BuildServiceProvider().GetRequiredService<IFeatureProvider>().IsEnabled("CheckoutLifecycleV2"));
    }

    [Fact]
    public async Task Handle_V2Only_PersistsNonEmptyProvenanceAndPublishesOneKeyedV2Outcome()
    {
        const string tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var stock = StockItem.Create(productId, Guid.NewGuid(), tenantId, 2, false, -100);
        Reservation? created = null;
        var reservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        reservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(null));
        reservations.AddAsync(Arg.Do<Reservation>(reservation => created = reservation), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var stockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        stockItems.ListAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>([stock]));
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedV2Handler.Handle(
            V2Event(orderId, basketId, tenantId, productId),
            stockItems,
            reservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            TimeProvider.System,
            FeatureProvider(enabled: true),
            bus,
            CancellationToken.None);

        Reservation persisted = Assert.IsType<Reservation>(created);
        Assert.True(persisted.IsLifecycleV2);
        Assert.Equal(basketId, persisted.BasketId);
        Assert.Equal("checkout-correlation", persisted.SourceCorrelationId);
        await bus.Received(1).PublishAsync(Arg.Is<StockReservedV2IntegrationEvent>(published =>
            published.OrderId == orderId
            && published.BasketId == basketId
            && published.SourceCorrelationId == "checkout-correlation"
            && published.IdempotencyKey == $"stock-reserved:{orderId:N}"));
    }

    [Fact]
    public async Task Handle_RejectsMissingV2ProvenanceBeforeMutation()
    {
        var evt = V2Event(Guid.NewGuid(), Guid.Empty, "tenant-1", Guid.NewGuid());
        var reservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => OrderPlacedV2Handler.Handle(
            evt,
            Substitute.For<IGenericWriteRepository<StockItem, Guid>>(),
            reservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            TimeProvider.System,
            FeatureProvider(enabled: true),
            Substitute.For<IMessageBus>(),
            CancellationToken.None));

        await reservations.DidNotReceive().AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RejectsEmptyV2CorrelationBeforeMutation()
    {
        var evt = V2Event(Guid.NewGuid(), Guid.NewGuid(), "tenant-1", Guid.NewGuid(), " ");
        var reservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();

        await Assert.ThrowsAsync<ArgumentException>(() => OrderPlacedV2Handler.Handle(
            evt,
            Substitute.For<IGenericWriteRepository<StockItem, Guid>>(),
            reservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            TimeProvider.System,
            FeatureProvider(enabled: true),
            Substitute.For<IMessageBus>(),
            CancellationToken.None));

        await reservations.DidNotReceive().AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_V2ThenV1ForSameOrder_PersistsOneReservationAndOneV2Outcome()
    {
        const string tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var stock = StockItem.Create(productId, Guid.NewGuid(), tenantId, 2, false, -100);
        Reservation? persisted = null;
        var reservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        reservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(persisted));
        reservations.AddAsync(Arg.Do<Reservation>(reservation => persisted = reservation), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var stockItems = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        stockItems.ListAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>([stock]));
        var priorities = Substitute.For<IGenericReadRepository<LocationPriority, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedV2Handler.Handle(
            V2Event(orderId, basketId, tenantId, productId),
            stockItems,
            reservations,
            priorities,
            unitOfWork,
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            TimeProvider.System,
            FeatureProvider(enabled: true),
            bus,
            CancellationToken.None);
        await OrderPlacedHandler.Handle(
            V1Event(orderId, tenantId, productId),
            stockItems,
            reservations,
            priorities,
            unitOfWork,
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            bus,
            CancellationToken.None);

        Reservation reservation = Assert.IsType<Reservation>(persisted);
        Assert.True(reservation.IsLifecycleV2);
        Assert.Equal(basketId, reservation.BasketId);
        await reservations.Received(1).AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservedV2IntegrationEvent>(published =>
            published.OrderId == orderId
            && published.IdempotencyKey == $"stock-reserved:{orderId:N}"));
    }

    [Fact]
    public async Task Handle_WhenLegacyReservationAlreadyExists_AdoptsLifecycleAndPublishesOneV2Outcome()
    {
        const string tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        Reservation legacy = Reservation.CreateCommitted(
            ReservationSource.Order,
            orderId,
            tenantId,
            [new ReservationLine(productId, 2, 0, [new Allocation(locationId, 2)])]);
        var reservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        reservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(legacy));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedV2Handler.Handle(
            V2Event(orderId, basketId, tenantId, productId),
            Substitute.For<IGenericWriteRepository<StockItem, Guid>>(),
            reservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(),
            unitOfWork,
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            TimeProvider.System,
            FeatureProvider(enabled: true),
            bus,
            CancellationToken.None);

        Assert.True(legacy.IsLifecycleV2);
        Assert.Equal(basketId, legacy.BasketId);
        Assert.Equal("checkout-correlation", legacy.SourceCorrelationId);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservedV2IntegrationEvent>(published =>
            published.ReservationId == legacy.Id
            && published.OrderId == orderId
            && published.BasketId == basketId
            && published.TenantId == tenantId
            && published.SourceCorrelationId == "checkout-correlation"
            && published.IdempotencyKey == $"stock-reserved:{orderId:N}"
            && published.Lines.Count == 1
            && published.Lines[0].ProductId == productId));
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WhenLifecycleFlagIsOff_AdoptsLegacyReservationWithoutV2Publication()
    {
        var orderId = Guid.NewGuid();
        Reservation legacy = Reservation.CreateCommitted(ReservationSource.Order, orderId, "tenant-1", []);
        var reservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        reservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(legacy));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedV2Handler.Handle(
            V2Event(orderId, Guid.NewGuid(), "tenant-1", Guid.NewGuid()),
            Substitute.For<IGenericWriteRepository<StockItem, Guid>>(), reservations,
            Substitute.For<IGenericReadRepository<LocationPriority, Guid>>(), unitOfWork,
            Substitute.For<IServiceScopeFactory>(), Options.Create(new InventoryOptions()), TimeProvider.System,
            FeatureProvider(enabled: false), bus, CancellationToken.None);

        Assert.True(legacy.IsLifecycleV2);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservedV2IntegrationEvent>());
    }

    private static OrderPlacedV2IntegrationEvent V2Event(
        Guid orderId,
        Guid basketId,
        string tenantId,
        Guid productId,
        string sourceCorrelationId = "checkout-correlation") => new()
    {
        OrderId = orderId,
        BasketId = basketId,
        TenantId = tenantId,
        SourceCorrelationId = sourceCorrelationId,
        Lines = [new OrderPlacedLine(productId, "Widget", 2, 5m, 10m)],
    };

    private static OrderPlacedIntegrationEvent V1Event(Guid orderId, string tenantId, Guid productId) => new()
    {
        OrderId = orderId,
        CustomerId = Guid.NewGuid(),
        TenantId = tenantId,
        Status = "Placed",
        Total = 10m,
        CreatedAt = DateTimeOffset.UtcNow,
        Lines = [new OrderPlacedLine(productId, "Widget", 2, 5m, 10m)],
    };

    private static IFeatureProvider FeatureProvider(bool enabled)
    {
        const string deploymentKey = "FeatureFlags__CheckoutLifecycleV2";
        lock (FeatureFlagEnvironmentLock)
        {
            string? originalValue = Environment.GetEnvironmentVariable(deploymentKey);
            try
            {
                Environment.SetEnvironmentVariable(deploymentKey, enabled.ToString());
                var configuration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();
                var services = new ServiceCollection();
                services.AddInventoryFeatureFlags(configuration);
                return services.BuildServiceProvider().GetRequiredService<IFeatureProvider>();
            }
            finally
            {
                Environment.SetEnvironmentVariable(deploymentKey, originalValue);
            }
        }
    }
}
